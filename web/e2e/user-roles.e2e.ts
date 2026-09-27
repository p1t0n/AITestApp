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
 *
 * Since EXP-46 the selector lives in the row's edit popup rather than in the row: the dictionary
 * convention is that a row is never an input. So each flow opens the popup first, and the column is
 * what it reads back afterwards.
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
    // The dictionary pages at ten, and this database keeps every account every spec has signed up,
    // so the row is found through the toolbar rather than assumed to be on the first page.
    const accounts = page.getByRole("table", { name: "Accounts" });
    const search = page.getByRole("textbox", { name: "Search email" });
    await search.fill(target);
    const row = accounts.getByRole("row").filter({ hasText: target });
    await expect(row.getByRole("cell").nth(1)).toHaveText("Administrator");

    await row.getByRole("button", { name: "Edit" }).click();
    await page.getByTestId("users-role-select").click();
    await page.getByRole("option", { name: "User" }).click();

    // A demotion asks first, and says what it costs.
    const confirm = page.getByTestId("users-role-confirm");
    await expect(confirm).toContainText(target);
    await expect(confirm).toContainText("their session ends immediately");
    await confirm.getByRole("button", { name: "Demote to User" }).click();
    await expect(confirm).toHaveCount(0);

    // The popup's own selector follows the list it was opened over, rather than the value it was
    // opened with — the page resolves the account being edited by id on every render. The frozen
    // hook sits on the field, whose text is the label as well as the value, so the assertion is on
    // the combobox inside it.
    await expect(page.getByTestId("users-role-select").getByRole("combobox")).toHaveText("User");
    await page.getByRole("button", { name: "Cancel" }).click();
    await expect(row.getByRole("cell").nth(1)).toHaveText("User");

    // The part only a reload can show: the server wrote it.
    await page.reload();
    await search.fill(target);
    const reloaded = accounts.getByRole("row").filter({ hasText: target });
    await expect(reloaded.getByRole("cell").nth(1)).toHaveText("User");
  });

  test("the actor's own row says why it cannot be changed, in text", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    const actor = await signUp(page);

    await page.goto("/users");
    await page.getByRole("textbox", { name: "Search email" }).fill(actor);
    const own = page
      .getByRole("table", { name: "Accounts" })
      .getByRole("row")
      .filter({ hasText: actor });
    await own.getByRole("button", { name: "Edit" }).click();

    // Both refusals apply to this account while they are the only Administrator; the self rule is
    // the one the page states, because it is the one that is true whoever else exists.
    const popup = page.getByRole("dialog");
    await expect(popup).toContainText("You cannot change your own role.");
    await expect(popup.getByTestId("users-role-select").locator("[aria-disabled=\"true\"]"))
      .toHaveCount(1);
  });
});
