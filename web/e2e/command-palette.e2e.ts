// The ⌘K palette in a real browser (P1T-165). Three of these are questions jsdom cannot answer:
// whether the browser lets the app have the keystroke at all, where focus lands when a modal opens,
// and whether the highlighted row is *painted* differently rather than merely classed differently.
// The rest of the behaviour is held by `src/components/CommandPalette.test.tsx`.
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

/** The palette's own input, named the same way the app names it. */
const INPUT = "Jump to a place, a person, or an agent surface";

/**
 * The real keystroke, through the browser's own shortcut handling. `ControlOrMeta` is Playwright's
 * platform-correct modifier, which is exactly what the app's "either modifier" rule has to survive.
 */
async function pressHotkey(page: Page) {
  await page.keyboard.press("ControlOrMeta+k");
}

function palette(page: Page) {
  return page.getByRole("combobox", { name: INPUT });
}

/** Resolved background of a row — what the cascade actually painted, not the class list. */
function backgroundOf(page: Page, name: string | RegExp) {
  return page
    .getByRole("option", { name })
    .evaluate((el) => getComputedStyle(el).backgroundColor);
}

test.describe("the command palette", () => {
  test.beforeEach(async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  test("opens on the real keystroke with focus in its input, and closes on Escape", async ({
    page,
  }) => {
    await expect(page.getByRole("link", { name: "CVs" })).toBeVisible();

    await pressHotkey(page);
    await expect(palette(page)).toBeVisible();
    // Chrome binds ⌘K/Ctrl+K to its own address-bar search, so this passing at all is the
    // `preventDefault` doing its job — and focus being *inside* the dialog is what makes the next
    // keystroke a search rather than a page scroll.
    await expect(palette(page)).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(palette(page)).toBeHidden();
  });

  test("the rail advertises the shortcut, and its row opens the same palette", async ({ page }) => {
    const trigger = page.getByRole("button", { name: "Search" });
    // The hint is the platform's own spelling; on the Linux/Chromium the suite runs it is Ctrl.
    await expect(trigger).toContainText(/⌘K|Ctrl K/);

    await trigger.click();
    await expect(palette(page)).toBeVisible();
  });

  test("the highlighted row is painted, not just classed", async ({ page }) => {
    await pressHotkey(page);
    await expect(palette(page)).toBeVisible();

    // The first row is highlighted from the moment it opens, so Enter alone is a whole gesture.
    // Two rows, two resolved colours: `Mui-selected` emitting a rule proves nothing about whether
    // it won, which is the standing lesson of this chain (`manuals/spa-design-system.md` §11).
    const first = await backgroundOf(page, "CVs");
    const second = await backgroundOf(page, "Skill Catalog");
    expect(first).not.toBe(second);
  });

  test("arrows and Enter reach a place without the mouse", async ({ page }) => {
    await page.goto("/catalog");
    await pressHotkey(page);
    await expect(palette(page)).toBeVisible();

    // Down twice from `CVs`: Skill Catalog, then Users.
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Enter");

    await expect(page).toHaveURL(/\/users$/);
    await expect(palette(page)).toBeHidden();
  });

  test("finds a person by name and jumps to them", async ({ page }) => {
    // This spec owns its own row, like every other spec against the shared e2e roster — a name no
    // other spec creates, or two rows would make every `getByRole("row", …)` in the suite ambiguous.
    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Evelyn");
    await dialog.getByLabel("Last name").fill("Granville");
    await dialog.getByLabel("Title").fill("Research Mathematician");
    await dialog.getByLabel("Email").fill(uniqueEmail("evelyn"));
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("row", { name: /Evelyn Granville/ })).toBeVisible();

    await page.goto("/catalog");
    await pressHotkey(page);
    await palette(page).fill("evelyn");

    await expect(page.getByRole("option", { name: /Evelyn Granville/ })).toBeVisible();
    await page.keyboard.press("Enter");

    await expect(page).toHaveURL(/\/experts\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { name: "Evelyn Granville" })).toBeVisible();
  });

  test("opens the agent dock on the surface it was asked for", async ({ page }) => {
    await pressHotkey(page);
    await palette(page).fill("interview");
    await page.getByRole("option", { name: /Interview kit/ }).click();

    // The dock was closed, so this is both halves of a Surface Request: it opened, and it opened
    // on the right surface. The picker's accessible name is where the dock says which one it is.
    await expect(page.getByRole("button", { name: "Agent surface: Interview kit" })).toBeVisible();
  });
});
