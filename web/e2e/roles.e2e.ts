import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, signUpAsExpert } from "./passkey";

/**
 * The role split at the real surface (P1T-181). Two facts the unit suites cannot show together: a
 * self-serve signup really is an Expert end to end (form → ceremony → token → route), and an Expert
 * who asks for a staff route is sent to **their own page** rather than to the sign-in screen.
 *
 * That second one is the whole reason the guard takes a role instead of a boolean: bouncing a
 * signed-in person to `/signin` tells them they are signed out and offers them a second account
 * they do not have.
 */
test.describe("role split", () => {
  test("a self-serve signup lands on the Expert's own workspace", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);

    await signUpAsExpert(page);

    await expect(page.getByRole("heading", { name: "My workspace" })).toBeVisible();
    // None of the staff chrome: the roster is other people's CVs.
    await expect(page.getByRole("link", { name: "CVs" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Users" })).toHaveCount(0);
  });

  test("an Expert asking for a staff route is redirected to their own landing page", async ({
    context,
    page,
  }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);

    await page.goto("/users");

    await expect(page).toHaveURL(/\/me$/);
    await expect(page.getByRole("heading", { name: "My workspace" })).toBeVisible();
    // The point of the redirect: not the gate.
    await expect(page.getByRole("heading", { name: "Sign in" })).toHaveCount(0);
  });

  test("the API refuses an Expert's session on a staff endpoint", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);

    // The token the browser is holding, sent at the API rather than through the SPA: the server
    // decides this, and it must not depend on the router having been polite about it.
    const status = await page.evaluate(async () => {
      const response = await fetch("/api/users", {
        headers: { Authorization: `Bearer ${localStorage.getItem("em.session.token")}` },
      });
      return response.status;
    });

    expect([401, 403]).toContain(status);
  });

  test("a Service Manager still lands on the roster", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);

    await signUp(page);

    await expect(page.getByRole("heading", { name: "CVs" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Users" })).toBeVisible();
  });
});
