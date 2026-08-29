import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

test.describe("passkey access", () => {
  test("a protected route with no session bounces to sign-in", async ({ page }) => {
    await page.goto("/");

    await expect(page).toHaveURL(/\/signin$/);
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    // The roster is the thing being guarded; none of it may leak onto the gate.
    await expect(page.getByRole("button", { name: "New CV" })).toHaveCount(0);
  });

  test("signing up registers a passkey and lands on the roster", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);

    await signUp(page);

    await expect(page.getByRole("heading", { name: "CVs" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
  });

  test("signing out and back in uses the assertion ceremony", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    const email = await signUp(page);

    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/signin$/);

    // The return visit is a different ceremony from registration — and a different half of
    // webauthn.ts — so it gets its own assertion rather than riding on signup's.
    await page.getByLabel("Email (optional)").fill(email);
    await page.getByRole("button", { name: /sign in with a passkey/i }).click();

    await expect(page.getByRole("heading", { name: "CVs" })).toBeVisible();
  });

  test("signing in with an unknown email fails without letting the caller through", async ({
    context,
    page,
  }) => {
    await addVirtualAuthenticator(context, page);

    await page.goto("/signin");
    await page.getByLabel("Email (optional)").fill(uniqueEmail("nobody"));
    await page.getByRole("button", { name: /sign in with a passkey/i }).click();

    await expect(page.getByRole("alert")).toBeVisible();
    await expect(page).toHaveURL(/\/signin$/);
  });
});
