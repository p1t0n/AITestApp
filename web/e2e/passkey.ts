import { execFileSync } from "node:child_process";
import { randomUUID } from "node:crypto";
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

// The e2e stack's own database, as created by run.mjs. Duplicated rather than imported because
// run.mjs owns the whole run (importing it would start a second stack).
const DB_CONTAINER = "experttojob-e2e-db";
const DB_NAME = "experttojob_e2e";

/**
 * Signs a brand-new **Service Manager** up through the real UI: the form, the registration
 * ceremony, and the redirect onto the roster. Returns the email so a test can sign the same account
 * back in.
 *
 * Staff, not an Expert, because that is what the rest of this suite is about — the roster, the
 * catalog, the agent dock. Since P1T-181 a self-serve signup is an Expert, so this pre-creates the
 * account the way an operator's own first sign-up works: the invite row the Service Manager
 * bootstrap writes (an address with no credential), which signup then adopts. That path is
 * production's, not a test-only door — see `api/Web/Auth/ServiceManagerBootstrapper.cs`.
 */
export async function signUp(page: Page, email = uniqueEmail("e2e")): Promise<string> {
  inviteServiceManager(email);
  await signUpThroughTheForm(page, email);
  await page.waitForURL("**/", { timeout: 30_000 });
  return email;
}

/**
 * Signs a brand-new Expert up — the plain self-serve path, with nothing pre-created. Lands on the
 * Expert's own workspace, which is the whole difference this helper exists to express.
 */
export async function signUpAsExpert(page: Page, email = uniqueEmail("expert")): Promise<string> {
  await signUpThroughTheForm(page, email);
  await page.waitForURL("**/me", { timeout: 30_000 });
  return email;
}

async function signUpThroughTheForm(page: Page, email: string): Promise<void> {
  await page.goto("/signup");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Control word").fill("correct horse battery staple");
  // Acknowledging the transparency notice is a condition of registering (P1T-183): the button
  // stays disabled without it and the server refuses `signup/begin` anyway. Doing it here rather
  // than in each test keeps the gate on the real path — every e2e signup goes through it.
  await page.getByLabel("I have read the notice above").check();
  await page.getByRole("button", { name: /sign up with a passkey/i }).click();
}

/**
 * Writes the invite row for a staff address straight into the run's database: an account with the
 * email, the ServiceManager role, and neither a passkey nor a control word — exactly what the
 * bootstrap creates for the configured first Service Manager. Signup adopts it and enrols the
 * passkey, so the account that results is a real one, made through the real ceremony.
 */
function inviteServiceManager(email: string): void {
  execFileSync("docker", [
    "exec", DB_CONTAINER,
    "psql", "-U", "postgres", "-d", DB_NAME, "-v", "ON_ERROR_STOP=1", "-c",
    `INSERT INTO "Users" ("Id", "Email", "ControlWordHash", "Status", "Role", "TokenVersion", "CreatedAt", "UpdatedAt")
     VALUES ('${randomUUID()}', '${email}', '', 'Active', 'ServiceManager', 1, now(), now());`,
  ], { stdio: "pipe" });
}
