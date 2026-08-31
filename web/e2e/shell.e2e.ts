// The app shell in a real browser (P1T-161). Everything here is a question the unit suite cannot
// answer, because every one of them is about what the *cascade* resolved to: a computed padding, a
// media query the browser evaluated, a rule that survived print. Slice 1's lesson is the reason
// this file exists — an override can emit perfect CSS and render nothing.
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

/** The routed area's own container: the element the two edges pad. */
const SHELL = "#root > div";

async function paddingOf(page: Page, side: "left" | "right"): Promise<number> {
  return page.$eval(SHELL, (el, s) =>
    Number.parseFloat(getComputedStyle(el).getPropertyValue(`padding-${s}`)), side);
}

/** The rail's own published width, as the document root carries it. */
async function railPush(page: Page): Promise<string> {
  return page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue("--app-rail-push").trim());
}

async function openTheDock(page: Page) {
  await page.getByRole("button", { name: "Open the agents assistant" }).click();
  // Docked, not floating: a floating bubble overlays on purpose and so covers nothing. The
  // widget's control is named by its Tooltip (`Dock to side` / `Float`), which MUI turns into the
  // accessible name.
  const dockIt = page.getByRole("button", { name: "Dock to side" });
  if (await dockIt.count()) await dockIt.click();
  await expect(page.getByRole("button", { name: "Float" })).toBeVisible();
}

/**
 * Both gutters animate (`transition: padding 150ms`), so every read here is polled rather than
 * instant. An instant read returns a mid-transition interpolation that looks exactly like a
 * failing rule — the first run of this file answered 83.38px for a 240px rail, which is the same
 * trap P1T-164 hit on `MuiPaper`'s `box-shadow`.
 */
function expectPadding(page: Page, side: "left" | "right") {
  return expect.poll(() => paddingOf(page, side));
}

test.describe("the shell's two edges", () => {
  test("the rail publishes what it covers, and the shell pads by it", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);

    await expect(page.getByRole("link", { name: "CVs" })).toBeVisible();
    expect(await railPush(page)).toBe("240px");
    // The whole contract in one assertion: the rail took no part in layout, and the content moved.
    await expectPadding(page, "left").toBe(240);
  });

  test("collapsing narrows the gutter, and survives a reload", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);

    await page.getByRole("button", { name: "Collapse the navigation rail" }).click();
    await expect(page.getByRole("button", { name: "Expand the navigation rail" })).toBeVisible();
    await expectPadding(page, "left").toBe(64);

    await page.reload();

    await expect(page.getByRole("button", { name: "Expand the navigation rail" })).toBeVisible();
    await expectPadding(page, "left").toBe(64);
    // Collapsed, but never nameless — this is the assertion the aria-labels exist for.
    await expect(page.getByRole("link", { name: "Skill Catalog" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
  });

  test("the rail yields to the dock at 1280 and stands its ground at 1440", async ({
    context,
    page,
  }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);
    await openTheDock(page);

    // Both edges push. At 1440 an expanded rail still leaves the content above its 720px floor…
    await expectPadding(page, "right").toBeGreaterThan(0);
    await expectPadding(page, "left").toBe(240);
    const dockPush = await paddingOf(page, "right");
    expect(1440 - dockPush - 240).toBeGreaterThanOrEqual(720);

    // …and at 1280 it does not, so the rail gives up its labels rather than the content its width.
    await page.setViewportSize({ width: 1280, height: 900 });
    await expectPadding(page, "left").toBe(64);
    expect(1280 - (await paddingOf(page, "right")) - 64).toBeGreaterThanOrEqual(720);
    // And it says why it cannot be expanded rather than offering a control that does nothing.
    await expect(page.getByRole("button", { name: "Expand the navigation rail" })).toBeDisabled();
  });

  test("below md the rail becomes a drawer that covers nothing", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);

    await page.setViewportSize({ width: 720, height: 900 });
    await expect(page.getByRole("button", { name: "Open the navigation" })).toBeVisible();
    await expectPadding(page, "left").toBe(0);

    await page.getByRole("button", { name: "Open the navigation" }).click();
    await page.getByRole("link", { name: "Users" }).click();

    await expect(page).toHaveURL(/\/users$/);
    // Navigating closed it: a temporary drawer that stays open over the page it just left is a bug.
    await expect(page.getByRole("link", { name: "Users" })).toBeHidden();
  });

  test("neither edge leaves a gutter on the printed page", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);
    await openTheDock(page);

    await expectPadding(page, "left").toBe(240);

    await page.emulateMedia({ media: "print" });

    await expectPadding(page, "left").toBe(0);
    await expectPadding(page, "right").toBe(0);
    // The rail itself is gone, not merely un-padded — and located by CSS rather than by role,
    // because a role locator will not match a `display: none` element and would simply hang.
    expect(await page.locator("a[aria-label='CVs']").boundingBox()).toBeNull();

    await page.emulateMedia({ media: "screen" });
  });

  test("the theme choice survives a reload and reaches a second tab", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);

    await page.getByRole("button", { name: "Theme" }).click();
    await page.getByRole("menuitem", { name: "Dark" }).click();

    // Read the resolved background rather than the stored string: the point is that the *theme*
    // swapped, not that a key was written.
    const dark = await page.$eval("body", (el) => getComputedStyle(el).backgroundColor);
    await page.reload();
    await expect(page.getByRole("link", { name: "CVs" })).toBeVisible();
    expect(await page.$eval("body", (el) => getComputedStyle(el).backgroundColor)).toBe(dark);

    // A second tab in the same context: the `storage` event is what carries the change across.
    const second = await context.newPage();
    await second.goto("/");
    await expect(second.getByRole("link", { name: "CVs" })).toBeVisible();
    expect(await second.$eval("body", (el) => getComputedStyle(el).backgroundColor)).toBe(dark);

    await second.getByRole("button", { name: "Theme" }).click();
    await second.getByRole("menuitem", { name: "Light" }).click();
    await expect
      .poll(() => page.$eval("body", (el) => getComputedStyle(el).backgroundColor))
      .not.toBe(dark);
    await second.close();
  });

  test("the rail is reachable from the keyboard, with a visible ring", async ({ context, page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    await addVirtualAuthenticator(context, page);
    await signUp(page);

    await page.goto("/");
    await expect(page.getByRole("link", { name: "CVs" })).toBeVisible();

    // Tabbed to, not `.focus()`ed: `:focus-visible` is a heuristic on *how* focus arrived, so a
    // programmatic focus renders no ring and would fail this assertion for the wrong reason.
    //
    // The palette's trigger is the rail's first row since P1T-165, so it is what the first Tab
    // reaches — the only assertion in this chain rewritten because the *fact* changed rather than
    // because a grip on the DOM slipped. The ring below is still measured on `CVs`, the frozen name
    // this test exists for.
    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => document.activeElement?.getAttribute("aria-label"))).toBe(
      "Search",
    );

    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => document.activeElement?.getAttribute("aria-label"))).toBe(
      "CVs",
    );

    const outline = await page.$eval("a[aria-label='CVs']", (el) => {
      const s = getComputedStyle(el);
      return { width: s.outlineWidth, style: s.outlineStyle };
    });
    // `html *:focus-visible`, the specificity finding from slice 1 — asserted on the resolved
    // value, because that is the only place the failure was ever visible.
    expect(outline.style).not.toBe("none");
    expect(Number.parseFloat(outline.width)).toBeGreaterThanOrEqual(2);

    // Tab moves along the rail rather than stopping at it.
    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => document.activeElement?.getAttribute("aria-label"))).toBe(
      "Skill Catalog",
    );
  });
});
