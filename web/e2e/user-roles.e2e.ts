import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

/**
 * Changing somebody's role at the real surface (P1T-239).
 *
 * The unit suite drives the selector against a mocked mutation, so what it cannot show is the part
 * that spans processes: that the chosen value reaches `PUT /api/users/{id}/role`, that the server
 * writes it, and that the column says the same thing after a reload — a change that only lived in
 * React state would pass every jsdom assertion in the repo and be gone on refresh.
 *
 * Two Administrators, because one is a refusal: the last one cannot be demoted, and an actor cannot
 * change their own role. The second account is invited and signed up through the real ceremony, the
 * same door `signUp` already uses for staff.
 */
test.describe("changing a role", () => {
  test("an Administrator demotes another account, and it sticks across a reload", async ({
    context,
    page,
  }) => {
    await addVirtualAuthenticator(context, page);

    // The account that gets demoted, signed up first so it exists as a real Administrator.
    const target = await signUp(page);

    // Then the one doing it, in a clean session — the demotion has to come from somebody else.
    await page.evaluate(() => localStorage.clear());
    await signUp(page);

    await page.goto("/users");
    const row = page.getByRole("row").filter({ hasText: target });
    await expect(row.getByTestId("users-role-select")).toHaveText("Administrator");

    await row.getByTestId("users-role-select").click();
    await page.getByRole("option", { name: "User" }).click();

    // A demotion asks first, and says what it costs.
    const confirm = page.getByTestId("users-role-confirm");
    await expect(confirm).toContainText(target);
    await expect(confirm).toContainText("their session ends immediately");
    await confirm.getByRole("button", { name: "Demote to User" }).click();
    await expect(confirm).toHaveCount(0);

    await expect(row.getByTestId("users-role-select")).toHaveText("User");

    // The part only a reload can show: the server wrote it.
    await page.reload();
    const reloaded = page.getByRole("row").filter({ hasText: target });
    await expect(reloaded.getByTestId("users-role-select")).toHaveText("User");
  });

  test("the actor's own row says why it cannot be changed, in text", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    const actor = await signUp(page);

    await page.goto("/users");
    const own = page.getByRole("row").filter({ hasText: actor });

    // Both refusals land on this row while they are the only Administrator; the self rule is the
    // one the page states, because it is the one that is true whoever else exists.
    await expect(own).toContainText("You cannot change your own role.");
  });
});
