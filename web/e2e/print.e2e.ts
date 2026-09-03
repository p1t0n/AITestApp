// The print cascade for everything outside the CV page (P1T-166).
//
// Why this file exists. `CvPage.print.test.tsx` and its siblings read the CSS the app *emits*:
// they brace-match `@media print{…}` blocks and check the block carries the element's own
// generated class. That proves a declaration was written and attached to the right element. It
// cannot prove the declaration *wins*, because jsdom implements no cascade and never re-evaluates
// a media query — P1T-159 paid for that lesson with a focus ring that emitted perfect CSS and
// rendered nothing, losing a specificity tie on source order.
//
// `emulateMedia({ media: "print" })` makes Chromium recompute the cascade against the print media
// type while keeping the DOM queryable, so `getComputedStyle` and `boundingBox()` report what the
// paper would carry. P1T-164 did this for the CV sheet in `cv-print.e2e.ts`, and P1T-161's
// `shell.e2e.ts` did it for the two gutters the rail and the dock publish. What was left with no
// coverage at all is the chrome those two specs step around: the **agent dock**, which is the one
// fixed surface in this app that had no print rule whatsoever, the rail's *mobile* path, and the
// slim bar a signed-out visitor gets.
//
// Two findings carried over from doing this once, both of which shape every assertion below:
//
//   * **`display` does not inherit.** A child of a `display: none` parent computes its own
//     `display` — the CV page's Print button reports `flex` at print media while being entirely
//     unrendered. So the load-bearing assertion is the *layout box*: "is it on the paper" is a
//     question only a layout engine answers. `display` is asserted only on the element that
//     actually carries the rule.
//   * **Some of this animates.** `MuiPaper` transitions `box-shadow`, so a media switch reads back
//     a mid-transition interpolation rather than the resolved value. Anything transitionable is
//     polled. `display` is not transitionable, which is why the reads here can be instant.
import { expect, test } from "@playwright/test";
import type { ElementHandle, Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

/**
 * Computed style through an `ElementHandle`, not a `Locator`. `getByRole` resolves against the
 * accessibility tree and a `display: none` element is not in it, so every role locator for this
 * chrome would time out at exactly the media where it has to be readable. Handles are captured on
 * screen media and stay valid across the switch.
 */
function css(handle: ElementHandle<Element>, prop: string): Promise<string> {
  return handle.evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), prop);
}

/** Whether the element occupies space in the layout — the only honest form of "it is on the paper". */
async function onPaper(handle: ElementHandle<Element>): Promise<boolean> {
  return (await handle.boundingBox()) !== null;
}

/** A handle for the first match of a CSS selector, non-null by construction. */
async function handleFor(page: Page, selector: string): Promise<ElementHandle<Element>> {
  const handle = await page.locator(selector).first().elementHandle();
  if (!handle) throw new Error(`Nothing matched ${selector}`);
  return handle;
}

/**
 * The dock panel's own root. Reached from the Token Ledger button's accessible name and walked out
 * to the outermost enclosing `Paper` — names are frozen (`manuals/spa-design-system.md` §9),
 * classes and nesting depth are not, so this survives the dock chrome being restyled.
 */
async function dockPanel(page: Page): Promise<ElementHandle<Element>> {
  const handle = await page
    .getByRole("button", { name: "Token usage" })
    .locator('xpath=ancestor::*[contains(@class,"MuiPaper-root")][last()]')
    .elementHandle();
  if (!handle) throw new Error("The dock panel was not found");
  return handle;
}

/** Signs a fresh account in and lands on the roster. Every test here needs the signed-in chrome. */
async function signInFresh(page: Page, context: Parameters<typeof addVirtualAuthenticator>[0]) {
  await page.goto("/signin");
  await addVirtualAuthenticator(context, page);
  await signUp(page);
  await expect(page.getByRole("link", { name: "CVs" })).toBeVisible();
}

test.describe("the print cascade outside the CV page", () => {
  test("the agent dock is not on the paper, as a bubble or as a docked panel", async ({
    context,
    page,
  }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await signInFresh(page, context);

    // The bubble first — the dock's closed state, and the one that is on screen on every route.
    const fab = (await page
      .getByRole("button", { name: "Open the agents assistant" })
      .elementHandle())!;
    expect(await onPaper(fab)).toBe(true);

    await page.emulateMedia({ media: "print" });
    // A `position: fixed` bubble is painted over the page, so without a print rule it lands in the
    // bottom-right corner of the first sheet of every printed artifact in this app, including a
    // client's CV. The layout box is the assertion; the rule it depends on is asserted next to it.
    expect(await onPaper(fab)).toBe(false);
    expect(await css(fab, "display")).toBe("none");

    await page.emulateMedia({ media: null });
    // A print rule that had leaked to screen would have passed everything above while deleting the
    // only way into the assistant.
    expect(await onPaper(fab)).toBe(true);
    expect(await css(fab, "display")).not.toBe("none");

    // And now the open, docked panel: 420px of chrome, `position: fixed`, with a left border.
    // P1T-160 is why the border matters — browsers drop background colours from print but keep
    // borders, so an unruled panel prints as a hairline column down the page even where its
    // surface colour vanishes.
    await page.getByRole("button", { name: "Open the agents assistant" }).click();
    const dockIt = page.getByRole("button", { name: "Dock to side" });
    if (await dockIt.count()) await dockIt.click();
    await expect(page.getByRole("button", { name: "Float" })).toBeVisible();

    const panel = await dockPanel(page);
    expect(await onPaper(panel)).toBe(true);

    await page.emulateMedia({ media: "print" });

    expect(await onPaper(panel)).toBe(false);
    expect(await css(panel, "display")).toBe("none");
    // Nothing inside it reaches the paper either. Asserted on the box rather than on the child's
    // own `display`, which is the inheritance finding above: this button computes `inline-flex`
    // while being unrendered.
    expect(await onPaper(await handleFor(page, '[aria-label="Token usage"]'))).toBe(false);

    await page.emulateMedia({ media: null });

    expect(await onPaper(panel)).toBe(true);
    await expect(page.getByRole("button", { name: "Token usage" })).toBeVisible();
  });

  test("no page content prints its relief, which is a grey smudge on paper", async ({
    context,
    page,
  }) => {
    // The floor slice ② added to `baseline.ts`. Every panel in the app is extruded now — the roster
    // is a Paper around a table — and a dual shadow on paper is a grey halo around every card. The
    // rail and the dock hide themselves; page *content* has to print, just flat.
    //
    // This is the half jsdom cannot answer. `components.test.tsx` proves the rule is emitted;
    // whether it beats the component override that put the relief there in the first place is a
    // cascade question, and `!important` in a `@media print` block is only a claim until Chromium
    // agrees with it.
    await page.setViewportSize({ width: 1440, height: 900 });
    await signInFresh(page, context);

    // The roster's own card: walked out from the table's first column header, whose name is frozen
    // (§9), rather than named by a class or a position that a later slice may move.
    const card = (await page
      .getByRole("columnheader", { name: "Name" })
      .locator('xpath=ancestor::*[contains(@class,"MuiPaper-root")][1]')
      .elementHandle())!;
    expect(await css(card, "box-shadow")).not.toBe("none");

    await page.emulateMedia({ media: "print" });
    // Polled: `MuiPaper` transitions `box-shadow`, so an instant read after a media switch lands
    // on an interpolation — the trap in this file's header, and the one `cv-print.e2e.ts` hit.
    await expect.poll(() => css(card, "box-shadow"), { timeout: 5_000 }).toBe("none");

    await page.emulateMedia({ media: null });
    // And it is a print rule, not a deletion: the relief comes back on screen.
    await expect.poll(() => css(card, "box-shadow"), { timeout: 5_000 }).not.toBe("none");
  });

  test("below md the rail's top bar and its drawer stay off the paper", async ({
    context,
    page,
  }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await signInFresh(page, context);

    // Narrow: the permanent rail becomes a slim `AppBar` plus a temporary drawer. `shell.e2e.ts`
    // proved this path covers no gutter; whether it prints is a different rule on a different
    // element, and it is the one that was never watched.
    await page.setViewportSize({ width: 720, height: 900 });
    await expect(page.getByRole("button", { name: "Open the navigation" })).toBeVisible();

    const bar = await handleFor(page, "header");
    expect(await onPaper(bar)).toBe(true);

    await page.emulateMedia({ media: "print" });
    expect(await onPaper(bar)).toBe(false);
    expect(await css(bar, "display")).toBe("none");

    await page.emulateMedia({ media: null });
    expect(await onPaper(bar)).toBe(true);

    // Now the drawer itself, open. A temporary drawer is a `fixed` overlay with a backdrop over
    // the whole viewport — if its print rule lost, the paper would be a grey wash with a nav on it.
    await page.getByRole("button", { name: "Open the navigation" }).click();
    await expect(page.getByRole("link", { name: "Skill Catalog" })).toBeVisible();

    const drawer = (await page
      .getByRole("navigation", { name: "Main" })
      .locator('xpath=ancestor::*[contains(@class,"MuiDrawer-root")][1]')
      .elementHandle())!;
    expect(await onPaper(drawer)).toBe(true);

    await page.emulateMedia({ media: "print" });
    expect(await onPaper(drawer)).toBe(false);
    expect(await css(drawer, "display")).toBe("none");
    expect(await onPaper(await handleFor(page, 'a[aria-label="Skill Catalog"]'))).toBe(false);

    await page.emulateMedia({ media: null });
    expect(await onPaper(drawer)).toBe(true);
  });

  test("the signed-out top bar does not print either", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/signin");
    // `Sign in` is a frozen accessible name, which is what makes this bar findable at all.
    await expect(page.getByRole("link", { name: "Sign in" })).toBeVisible();

    const bar = await handleFor(page, "header");
    expect(await onPaper(bar)).toBe(true);

    await page.emulateMedia({ media: "print" });
    expect(await onPaper(bar)).toBe(false);
    expect(await css(bar, "display")).toBe("none");

    await page.emulateMedia({ media: null });
    expect(await onPaper(bar)).toBe(true);
  });
});
