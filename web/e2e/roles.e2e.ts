import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, signUpAsExpert, uniqueEmail } from "./passkey";

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
  test("a self-serve signup lands on the Expert's own CV", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);

    await signUpAsExpert(page);

    // Nothing matched their address, so registration created a record that is theirs immediately
    // (P1T-184) — and editing it is what they came to do (P1T-190).
    await expect(page).toHaveURL(/\/me\/cv$/);
    await expect(page.getByRole("heading", { name: "My CV" })).toBeVisible();

    // Exactly two places, and none of the staff chrome: the roster is other people's CVs.
    await expect(page.getByRole("link", { name: "My CV" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Privacy & data" })).toBeVisible();
    for (const staffPlace of ["CVs", "Skill Catalog", "Users"]) {
      await expect(page.getByRole("link", { name: staffPlace })).toHaveCount(0);
    }
  });

  test("an Expert asking for a staff route is redirected to their own landing page", async ({
    context,
    page,
  }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);

    await page.goto("/users");

    await expect(page).toHaveURL(/\/me\/cv$/);
    await expect(page.getByRole("heading", { name: "My CV" })).toBeVisible();
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

  /**
   * The other landing (P1T-190). Somebody whose address matches a record a Service Manager already
   * entered does not get that record — an email proves nothing here, so a person has to confirm it
   * (P1T-184). Until then they own nothing, and an empty CV editor would tell them the opposite of
   * what is happening.
   */
  test("somebody whose claim is waiting lands on claim status, not an empty editor", async ({
    context,
    page,
  }) => {
    await addVirtualAuthenticator(context, page);
    const benchEmail = uniqueEmail("bench");

    // A Service Manager puts them on the bench first.
    await signUp(page);
    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Unclaimed");
    await dialog.getByLabel("Last name").fill("Person");
    await dialog.getByLabel("Title").fill("Engineer");
    await dialog.getByLabel("Email").fill(benchEmail);
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toHaveCount(0);

    // Then that person signs up with the same address, in a clean session.
    await page.evaluate(() => localStorage.clear());
    await signUpAsExpert(page, benchEmail);

    await expect(page).toHaveURL(/\/me\/claim$/);
    await expect(page.getByRole("heading", { name: "Your record" })).toBeVisible();
    await expect(page.getByText(/no record here yet under your name/)).toBeVisible();
    // Not an editor pretending there is something to fill in.
    await expect(page.getByRole("heading", { name: "My CV" })).toHaveCount(0);
  });

  test("a Service Manager still lands on the roster", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);

    await signUp(page);

    await expect(page.getByRole("heading", { name: "CVs" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Users" })).toBeVisible();
  });
});
