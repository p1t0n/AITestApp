import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Narrow the roster to one person and hand back their row.
 *
 * Since EXP-45 the Experts page is paged — 25 rows a screen, ordered by last name — so
 * `getByRole("row", …)` straight after a create is a bet that the roster is still short enough and
 * that the new name sorts early enough. It holds today (the suite's shared database runs at about
 * twenty rows) and it would stop holding the day somebody adds a spec that seeds ten more, with a
 * failure that reads as "the row is missing" rather than "the row is on page two".
 *
 * Searching first removes the bet. It also means every create-then-open spec drives the new search
 * box against a real server on its way past, which no unit test can do.
 */
export async function findInRoster(page: Page, fullName: string): Promise<Locator> {
  await page.getByLabel("Search the roster").fill(fullName);

  const row = page.getByRole("row", { name: new RegExp(fullName) });
  await expect(row).toBeVisible();
  return row;
}

/**
 * Reach one row's action through its ⋮ menu — the only route to any of them since EXP-50.
 *
 * The menu is a portal, so it is never inside the row the button is in: the click starts at the
 * row and the item is looked for on the page. Nothing here is a `data-testid`; the items are named
 * exactly as somebody reads them.
 */
export async function rowAction(page: Page, row: Locator, action: string): Promise<void> {
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: action, exact: true }).click();
}
