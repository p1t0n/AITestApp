import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";
import { findInRoster, rowAction } from "./roster";

test.describe("roster round trip", () => {
  test.beforeEach(async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  test("a new CV created in the UI is listed, opens, and renders as a CV", async ({ page }) => {
    const expertEmail = uniqueEmail("ada");

    await page.getByRole("button", { name: "New expert" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Ada");
    await dialog.getByLabel("Last name").fill("Lovelace");
    await dialog.getByLabel("Title").fill("Analytical Engineer");
    await dialog.getByLabel("Email").fill(expertEmail);
    await dialog.getByLabel("Location").fill("London");
    await dialog.getByLabel("Summary").fill("First programmer.");
    await dialog.getByRole("button", { name: "Save" }).click();

    const row = await findInRoster(page, "Ada Lovelace");
    // Title and location moved under the name rather than into columns of their own (EXP-45).
    await expect(row).toContainText("Analytical Engineer · London");

    // The row navigates to the detail page, and the detail page to the CV.
    await row.getByText("Ada Lovelace").click();
    await expect(page).toHaveURL(/\/experts\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { name: "Ada Lovelace" })).toBeVisible();

    await page.goto(`${page.url()}/cv`);
    await expect(page.getByRole("heading", { name: "Ada Lovelace" })).toBeVisible();
    await expect(page.getByText("First programmer.")).toBeVisible();
    await expect(page.getByRole("button", { name: /download pdf/i })).toBeVisible();
  });

  test("the CV page downloads a PDF from the server", async ({ page }) => {
    const expertEmail = uniqueEmail("grace");

    await page.getByRole("button", { name: "New expert" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Grace");
    await dialog.getByLabel("Last name").fill("Hopper");
    await dialog.getByLabel("Title").fill("Rear Admiral");
    await dialog.getByLabel("Email").fill(expertEmail);
    await dialog.getByRole("button", { name: "Save" }).click();

    await rowAction(page, await findInRoster(page, "Grace Hopper"), "View CV");
    await expect(page).toHaveURL(/\/cv$/);

    // The button fetches the bytes with the session token and hands them to the browser, so a
    // download event is the only proof the whole path works from the browser's side.
    const downloadStarted = page.waitForEvent("download", { timeout: 20_000 });
    await page.getByRole("button", { name: /download pdf/i }).click();
    const download = await downloadStarted;

    expect(download.suggestedFilename()).toBe("grace-hopper-cv.pdf");
  });

  /**
   * The whole popup-only path, in a real browser (EXP-50).
   *
   * Every assertion here was unreachable before: the row's delete was a `window.confirm`, which
   * Playwright can only dismiss blind, and the edit dialog had no guard to refuse anything. What
   * this proves that jsdom cannot is that the ⋮ menu, the dialog on top of it and the confirmation
   * on top of *that* stack and take focus in the order a person would need them to.
   *
   * Mary Jackson is this test's own row, by the suite's usual rule — `workers: 1` against one
   * shared roster, so a name another spec also creates makes *its* locator ambiguous rather than
   * this one's. She is deleted again on the way out, which is the point of the test.
   */
  test("edits and deletes an expert through the row menu, and never off the row", async ({
    page,
  }) => {
    await page.getByRole("button", { name: "New expert" }).click();
    const form = page.getByRole("dialog");
    await form.getByLabel("First name").fill("Mary");
    await form.getByLabel("Last name").fill("Jackson");
    await form.getByLabel("Title").fill("Research Mathematician");
    await form.getByLabel("Email").fill(uniqueEmail("jackson"));
    await form.getByRole("button", { name: "Save" }).click();

    const row = await findInRoster(page, "Mary Jackson");
    // Nothing on the row writes. The one control it carries is the menu.
    await expect(row.getByRole("button")).toHaveCount(1);

    // Refine narrows what is already on screen and says it only did that.
    await page.getByLabel("Refine these results").fill("Mary Jackson");
    await expect(page.getByText(/ · 1 after refine$/)).toBeVisible();
    await page.getByLabel("Refine these results").fill("");

    await rowAction(page, row, "Edit…");
    const edit = page.getByRole("dialog", { name: /Edit Mary Jackson/ });
    await edit.getByLabel("Title").fill("Space Scientist");

    // Dirty: Esc is refused and the question is asked instead.
    await page.keyboard.press("Escape");
    await expect(edit.getByText("Discard your changes?")).toBeVisible();
    await edit.getByRole("button", { name: "Keep editing" }).click();
    await edit.getByRole("button", { name: "Save" }).click();

    await expect(await findInRoster(page, "Mary Jackson")).toContainText("Space Scientist");

    // And delete, from inside the dialog, behind its own confirmation.
    await rowAction(page, await findInRoster(page, "Mary Jackson"), "Edit…");
    await page.getByRole("button", { name: "Delete" }).click();
    const confirm = page.getByRole("dialog", { name: "Delete Mary Jackson?" });
    await confirm.getByRole("button", { name: "Delete expert" }).click();

    await page.getByLabel("Search the roster").fill("Mary Jackson");
    await expect(page.getByText("No experts match.")).toBeVisible();
  });

  test("a validation failure from the API is shown in the dialog, not swallowed", async ({
    page,
  }) => {
    await page.getByRole("button", { name: "New expert" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("");
    await dialog.getByLabel("Last name").fill("Nameless");
    await dialog.getByLabel("Email").fill("not-an-email");
    await dialog.getByRole("button", { name: "Save" }).click();

    await expect(dialog.getByRole("alert")).toBeVisible();
    // The dialog stays open with the input intact, so the user can fix it.
    await expect(dialog.getByLabel("Last name")).toHaveValue("Nameless");
  });
});
