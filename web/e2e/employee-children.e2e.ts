import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

/**
 * The languages / qualifications / experiences forms (P1T-142).
 *
 * These endpoints had no caller in the SPA until this slice, and one of them was dead for its whole
 * life: `ExperiencesController` had no `[ApiController]`, so every JSON POST and PUT bound an empty
 * DTO and answered 400 (found by P1T-140). Component tests mock the API and would not have caught
 * it. Driving the forms through a real browser against a real API is what does.
 */
test.describe("employee child editing", () => {
  test.beforeEach(async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  /** Creates an employee through the roster dialog and lands on their detail page. */
  async function createEmployee(page: import("@playwright/test").Page, first: string, last: string) {
    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill(first);
    await dialog.getByLabel("Last name").fill(last);
    await dialog.getByLabel("Title").fill("Staff Engineer");
    await dialog.getByLabel("Email").fill(uniqueEmail(first.toLowerCase()));
    await dialog.getByRole("button", { name: "Save" }).click();

    const row = page.getByRole("row", { name: new RegExp(`${first} ${last}`) });
    await row.getByRole("cell", { name: `${first} ${last}` }).click();
    await expect(page).toHaveURL(/\/employees\/[0-9a-f-]{36}$/);
    return page.url();
  }

  test("an experience with bullets and a skill is written, edited, and reaches the CV", async ({
    page,
  }) => {
    const detailUrl = await createEmployee(page, "Barbara", "Liskov");

    await page.getByRole("button", { name: "Add experience" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Company").fill("Contoso");
    await dialog.getByLabel("Job title").fill("Principal Engineer");
    await dialog.getByLabel("Start date").fill("2019-04-01");
    await dialog.getByLabel("Summary").fill("Owned the substitution layer.");

    await dialog.getByRole("button", { name: "Add bullet" }).click();
    // Exact, because "Bullet 1" is a substring of the row's own "Move bullet 1 up" controls.
    await dialog
      .getByLabel("Bullet 1", { exact: true })
      .fill("Cut deploy time from an hour to eight minutes.");
    await dialog.getByRole("button", { name: "Add bullet" }).click();
    await dialog
      .getByLabel("Bullet 2", { exact: true })
      .fill("Led the migration off the shared database.");

    // The catalog is whatever the dev seed put there, so take the first option rather than
    // hard-coding a skill name the seed is free to change.
    await dialog.getByLabel("Skills").click();
    const firstSkill = await page.getByRole("option").first().textContent();
    await page.getByRole("option").first().click();
    await dialog.getByRole("button", { name: "Save" }).click();

    await expect(dialog).toBeHidden();
    await expect(page.getByText("Principal Engineer · Contoso")).toBeVisible();
    await expect(page.getByText("Cut deploy time from an hour to eight minutes.")).toBeVisible();
    await expect(page.getByText("Led the migration off the shared database.")).toBeVisible();

    // Edit: the PUT replaces the whole child collection, so a reordered bullet list is the
    // sharpest thing to assert — it proves the achievements travelled, not just the scalars.
    await page.getByRole("button", { name: "Edit Principal Engineer at Contoso" }).click();
    const editDialog = page.getByRole("dialog");
    await expect(editDialog.getByLabel("Company")).toHaveValue("Contoso");
    await editDialog.getByLabel("Move bullet 2 up").click();
    await editDialog.getByLabel("Job title").fill("Distinguished Engineer");
    await editDialog.getByRole("button", { name: "Save" }).click();

    await expect(editDialog).toBeHidden();
    await expect(page.getByText("Distinguished Engineer · Contoso")).toBeVisible();

    await page.goto(`${detailUrl}/cv`);
    await expect(page.getByText("Distinguished Engineer · Contoso")).toBeVisible();
    // The CV orders bullets by the order the form assigned from their position.
    const bullets = page.locator("#cv-sheet li");
    await expect(bullets.first()).toContainText("Led the migration off the shared database.");
    await expect(bullets.nth(1)).toContainText("Cut deploy time from an hour to eight minutes.");
    if (firstSkill) await expect(page.getByText(firstSkill, { exact: false }).first()).toBeVisible();
  });

  test("a language and a qualification round trip to the CV, and delete removes them", async ({
    page,
  }) => {
    const detailUrl = await createEmployee(page, "Grace", "Murray");

    await page.getByRole("button", { name: "Add language" }).click();
    const langDialog = page.getByRole("dialog");
    await langDialog.getByLabel("Language").fill("German");
    await langDialog.getByLabel("Level").click();
    await page.getByRole("option", { name: "Native" }).click();
    await langDialog.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("German · Native")).toBeVisible();

    await page.getByRole("button", { name: "Add qualification" }).click();
    const qualDialog = page.getByRole("dialog");
    await qualDialog.getByLabel("Name").fill("BSc Computer Science");
    await qualDialog.getByLabel("Institution").fill("Yale");
    await qualDialog.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("BSc Computer Science")).toBeVisible();

    await page.goto(`${detailUrl}/cv`);
    await expect(page.getByText("German (Native)")).toBeVisible();
    await expect(page.getByText(/BSc Computer Science · Yale/)).toBeVisible();

    await page.goto(detailUrl);
    await page.getByRole("button", { name: "Delete BSc Computer Science" }).click();
    await expect(page.getByText("BSc Computer Science")).toBeHidden();
  });

  test("a validation failure from the API is shown in the child dialog, not swallowed", async ({
    page,
  }) => {
    await createEmployee(page, "Alan", "Turing");

    // Company and Title are NotEmpty in SaveExperienceValidator; the server is the only validator
    // the form has, so an empty save must come back as a visible message.
    await page.getByRole("button", { name: "Add experience" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Start date").fill("2020-01-01");
    await dialog.getByRole("button", { name: "Save" }).click();

    await expect(dialog.getByRole("alert")).toBeVisible();
    // The dialog stays open with the input intact, so the user can fix it.
    await expect(dialog.getByLabel("Start date")).toHaveValue("2020-01-01");
  });
});
