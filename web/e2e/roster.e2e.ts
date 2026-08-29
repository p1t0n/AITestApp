import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

test.describe("roster round trip", () => {
  test.beforeEach(async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  test("a new CV created in the UI is listed, opens, and renders as a CV", async ({ page }) => {
    const employeeEmail = uniqueEmail("ada");

    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Ada");
    await dialog.getByLabel("Last name").fill("Lovelace");
    await dialog.getByLabel("Title").fill("Analytical Engineer");
    await dialog.getByLabel("Email").fill(employeeEmail);
    await dialog.getByLabel("Location").fill("London");
    await dialog.getByLabel("Summary").fill("First programmer.");
    await dialog.getByRole("button", { name: "Save" }).click();

    const row = page.getByRole("row", { name: /Ada Lovelace/ });
    await expect(row).toBeVisible();
    await expect(row).toContainText("Analytical Engineer");
    await expect(row).toContainText("London");

    // The row navigates to the detail page, and the detail page to the CV.
    await row.getByRole("cell", { name: "Ada Lovelace" }).click();
    await expect(page).toHaveURL(/\/employees\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { name: "Ada Lovelace" })).toBeVisible();

    await page.goto(`${page.url()}/cv`);
    await expect(page.getByRole("heading", { name: "Ada Lovelace" })).toBeVisible();
    await expect(page.getByText("First programmer.")).toBeVisible();
    await expect(page.getByRole("button", { name: /download pdf/i })).toBeVisible();
  });

  test("the CV page downloads a PDF from the server", async ({ page }) => {
    const employeeEmail = uniqueEmail("grace");

    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Grace");
    await dialog.getByLabel("Last name").fill("Hopper");
    await dialog.getByLabel("Title").fill("Rear Admiral");
    await dialog.getByLabel("Email").fill(employeeEmail);
    await dialog.getByRole("button", { name: "Save" }).click();

    await page.getByRole("row", { name: /Grace Hopper/ }).getByTitle("View CV").click();
    await expect(page).toHaveURL(/\/cv$/);

    // The button fetches the bytes with the session token and hands them to the browser, so a
    // download event is the only proof the whole path works from the browser's side.
    const downloadStarted = page.waitForEvent("download", { timeout: 20_000 });
    await page.getByRole("button", { name: /download pdf/i }).click();
    const download = await downloadStarted;

    expect(download.suggestedFilename()).toBe("grace-hopper-cv.pdf");
  });

  test("a validation failure from the API is shown in the dialog, not swallowed", async ({
    page,
  }) => {
    await page.getByRole("button", { name: "New CV" }).click();
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
