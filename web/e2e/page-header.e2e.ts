// The page header in a real browser (P1T-162). Two claims the unit suite structurally cannot make.
//
// **Sticky.** `position: sticky` is not a property you can read and believe — it is a behaviour of
// a scroll container, and jsdom has none. The unit spec proves the declaration is `sticky` and that
// the border follows a measured gap; only a browser can say the strip actually stopped moving, that
// it stopped at the *right* place (under the rail's mobile bar, per the published inset), and that
// the roster does not show through it.
//
// **Width.** The per-page cap replaced a global `Container maxWidth="lg"`, and the whole reason it
// exists is that the shell's two edges take room out of the middle column. What a page is left with
// is arithmetic the cascade does — so it is read off the cascade, with the dock open and closed.
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

/** The page's `<h1>`. Located by role and by frozen name; classes are not part of any contract. */
function heading(page: Page, name: string) {
  return page.getByRole("heading", { level: 1, name });
}

/** The sticky strip: the header's root, which is a `Stack`, reached from the heading upward. */
function stripBox(page: Page) {
  return page.locator("h1").locator("xpath=ancestor::*[contains(@class,'MuiStack-root')][1]");
}

/** The page's own box — the element carrying the per-page width cap. */
function pageBox(page: Page) {
  return page.locator("h1").locator("xpath=ancestor::*[contains(@class,'MuiContainer-root')][1]");
}

/**
 * One row of this spec's own. The suite runs `workers: 1` against one shared roster and specs keep
 * apart by owning the rows they create — and so does each test in this file, since the roster is
 * global while the account is not. Taken already: Ada Lovelace, Grace Hopper, Grace Murray,
 * Barbara Liskov, Katherine Johnson, Alan Turing, Dorothy Vaughan.
 */
async function seed(page: Page, first: string, last: string) {
  await page.getByRole("button", { name: "New CV" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("First name").fill(first);
  await dialog.getByLabel("Last name").fill(last);
  await dialog.getByLabel("Title").fill("Distinguished Engineer");
  await dialog.getByLabel("Email").fill(uniqueEmail("hdr"));
  await dialog.getByRole("button", { name: "Save" }).click();
  await expect(page.getByRole("row", { name: new RegExp(`${first} ${last}`) })).toBeVisible();
}

test.describe("the page header", () => {
  test.beforeEach(async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  test("pins to the top of a scrolled roster and gains its border there", async ({ page }) => {
    await seed(page, "Hedy", "Lamarr");

    // A short viewport rather than a long roster: how many rows other specs have left in the shared
    // roster is not something to depend on, and one row of this spec's own plus 200px of viewport
    // is a page that scrolls whatever else is in the table.
    await page.setViewportSize({ width: 1440, height: 200 });
    const strip = stripBox(page);
    await expect(heading(page, "CVs")).toBeVisible();
    expect(
      await page.evaluate(() => document.documentElement.scrollHeight > window.innerHeight),
    ).toBe(true);

    // At rest the strip sits below the top of the viewport, and its border is transparent — always
    // 1px, so gaining it later moves nothing down.
    expect((await strip.boundingBox())!.y).toBeGreaterThan(0);
    await expect(strip).not.toHaveAttribute("data-pinned", "true");
    const rest = await strip.evaluate((el) => getComputedStyle(el).borderBottomColor);
    expect(rest).toBe("rgba(0, 0, 0, 0)");

    await page.mouse.wheel(0, 600);

    // Pinned: the rail stands beside the app at 1440px, so the published top inset is 0 and the
    // strip stops flush against the viewport.
    await expect.poll(async () => (await strip.boundingBox())!.y).toBe(0);
    await expect(strip).toHaveAttribute("data-pinned", "true");
    // Polled, not read: the border colour crosses P1T-159's 150ms transition, and an instant read
    // lands on an interpolation — the same trap `cv-print.e2e.ts` hit on `box-shadow`.
    await expect
      .poll(() => strip.evaluate((el) => getComputedStyle(el).borderBottomColor))
      .not.toBe(rest);

    // And it is opaque: a table scrolling *through* the strip is the failure this exists to stop.
    const alpha = await strip.evaluate((el) => {
      const bg = getComputedStyle(el).backgroundColor;
      return Number.parseFloat(/rgba\([^)]*?,\s*([\d.]+)\)/.exec(bg)?.[1] ?? "1");
    });
    expect(alpha).toBe(1);
    await expect(heading(page, "CVs")).toBeVisible();
  });

  test("pins under the rail's mobile bar, which is the inset the rail publishes", async ({
    page,
  }) => {
    await seed(page, "Radia", "Perlman");

    // Below `md` the rail becomes a drawer behind a slim sticky bar. The header must stop under it,
    // and it learns how far down from `--app-rail-top-inset` rather than from the rail's state.
    await page.setViewportSize({ width: 720, height: 200 });
    await expect(page.getByRole("button", { name: "Open the navigation" })).toBeVisible();

    const inset = await page.evaluate(() =>
      Number.parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue("--app-rail-top-inset"),
      ),
    );
    expect(inset).toBeGreaterThan(0);

    await page.mouse.wheel(0, 600);

    const strip = stripBox(page);
    await expect.poll(async () => (await strip.boundingBox())!.y).toBe(inset);
    // Under the bar, not over it: the bar is the one thing allowed to cover the header.
    const barZ = await page
      .locator(".MuiAppBar-root")
      .evaluate((el) => Number(getComputedStyle(el).zIndex));
    const stripZ = await strip.evaluate((el) => Number(getComputedStyle(el).zIndex));
    expect(barZ).toBeGreaterThan(stripZ);
  });

  test("gives a table more room than a profile, and both give room back to the dock", async ({
    page,
  }) => {
    await seed(page, "Margaret", "Hamilton");
    await expect(heading(page, "CVs")).toBeVisible();
    const roster = (await pageBox(page).boundingBox())!;

    // The roster is `wide`; `lg` used to cap it at 1200 for everybody. At 1440 with a 240px rail
    // there is 1200 left over, so the table now takes what the shell leaves rather than sitting
    // inside whitespace the columns never get.
    expect(await pageBox(page).evaluate((el) => getComputedStyle(el).maxWidth)).toBe("1440px");
    expect(roster.width).toBeGreaterThan(1000);

    await page
      .getByRole("row", { name: /Margaret Hamilton/ })
      .getByRole("cell", { name: "Margaret Hamilton" })
      .click();
    await expect(heading(page, "Margaret Hamilton")).toBeVisible();

    // The profile is read rather than scanned, and says so at the top of its own file.
    expect(await pageBox(page).evaluate((el) => getComputedStyle(el).maxWidth)).toBe("1000px");
    expect((await pageBox(page).boundingBox())!.width).toBeLessThan(roster.width);

    // Docking the assistant takes room out of the middle column, and the page gives it up — which
    // is the thing a global container centred inside the leftovers could not do honestly.
    await page.goto("/");
    await expect(heading(page, "CVs")).toBeVisible();
    await page.getByRole("button", { name: "Open the agents assistant" }).click();
    const dockIt = page.getByRole("button", { name: "Dock to side" });
    if (await dockIt.count()) await dockIt.click();
    await expect(page.getByRole("button", { name: "Float" })).toBeVisible();

    await expect
      .poll(async () => (await pageBox(page).boundingBox())!.width)
      .toBeLessThan(roster.width);
  });

  test("prints the CV and none of its own chrome", async ({ page }) => {
    await seed(page, "Annie", "Easley");
    await page.getByRole("row", { name: /Annie Easley/ }).getByTitle("View CV").click();
    await expect(page).toHaveURL(/\/cv$/);

    // The page's own title is `CV`; the sheet's heading is the person. Two headings named the same
    // thing would make the page's subject ambiguous — to a screen reader, and to the three specs
    // that prove the sheet rendered by asking for a heading by name.
    await expect(heading(page, "CV")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Annie Easley" })).toHaveCount(1);

    const strip = (await stripBox(page).elementHandle())!;
    const box = (await pageBox(page).elementHandle())!;

    await page.emulateMedia({ media: "print" });

    // The strip goes; the box around the sheet does not, or nothing would print at all. And the box
    // drops its cap and its gutters, so the sheet is the page rather than an inset panel.
    expect(await strip.evaluate((el) => getComputedStyle(el).display)).toBe("none");
    expect(await box.evaluate((el) => getComputedStyle(el).display)).not.toBe("none");
    expect(await box.evaluate((el) => getComputedStyle(el).paddingLeft)).toBe("0px");
    expect(await page.locator("#cv-sheet").boundingBox()).not.toBeNull();

    await page.emulateMedia({ media: null });
    expect(await strip.evaluate((el) => getComputedStyle(el).display)).not.toBe("none");
  });
});
