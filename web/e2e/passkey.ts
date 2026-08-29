import type { BrowserContext, Page } from "@playwright/test";

/**
 * Attaches a virtual authenticator to the page's browser context and returns its id.
 *
 * The SPA is passkey-gated end to end, so without this every e2e test would stop at the sign-in
 * screen. `WebAuthn.addVirtualAuthenticator` is a Chrome DevTools Protocol command: it installs a
 * software authenticator that approves ceremonies without any user gesture, so registration and
 * assertion both run to completion headlessly. The credentials it holds live in the browser
 * context, which is what lets a test sign out and sign back in with the same passkey.
 */
export async function addVirtualAuthenticator(context: BrowserContext, page: Page): Promise<string> {
  const cdp = await context.newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  const { authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      // No gesture is possible in a headless run; the authenticator approves on its own.
      automaticPresenceSimulation: true,
    },
  });
  return authenticatorId;
}

/** A fresh address per run — accounts accumulate in the e2e database within a single run. */
export function uniqueEmail(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.com`;
}

/**
 * Signs a brand-new account up through the real UI: the form, the registration ceremony, and the
 * redirect onto the roster. Returns the email so a test can sign the same account back in.
 */
export async function signUp(page: Page, email = uniqueEmail("e2e")): Promise<string> {
  await page.goto("/signup");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Control word").fill("correct horse battery staple");
  await page.getByRole("button", { name: /sign up with a passkey/i }).click();
  await page.waitForURL("**/", { timeout: 30_000 });
  return email;
}
