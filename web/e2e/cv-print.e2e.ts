// The light-lock (P1T-164) at the surface that actually matters: a real Chromium, at real print
// media, in dark mode.
//
// Why the unit spec is not enough. `CvSheet.lightLock.test.tsx` proves the sheet resolves light
// colours in jsdom, which answers "did the nested provider take". It cannot answer the question this
// ticket is actually about, because the artifact a client receives is made by the *print* cascade,
// and jsdom implements no media queries at all — `CvPage.print.test.tsx` reads emitted CSS strings
// for exactly that reason, and an emitted declaration is not a winning one. P1T-159 already paid for
// that lesson: a focus ring that emitted perfect CSS and rendered nothing, because
// `*:focus-visible` lost a specificity tie on source order. Only a browser knows.
//
// `emulateMedia({ media: "print" })` makes Chromium re-evaluate the cascade against the print media
// type while keeping the DOM queryable, so `getComputedStyle` reports what the printed page would
// be. That is the whole point: the bad artifact this prevents is invisible on screen.
import { expect, test } from "@playwright/test";
import type { ElementHandle } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

/**
 * The light tokens the sheet must resolve to, as Chromium reports colours. Kept literal rather than
 * imported from `src/theme/tokens.ts`: a spec that read the same constant the app does would still
 * pass if a token were re-pointed at a dark value, which is one of the things it exists to catch.
 */
const LIGHT = { paper: "rgb(255, 255, 255)", text: "rgb(16, 20, 24)" };

/** The dark mode's near-white body text — the colour a print must never put on white paper. */
const DARK_TEXT = "rgb(230, 237, 243)";

/**
 * Computed style through an `ElementHandle`, not a `Locator`. `getByRole` resolves against the
 * accessibility tree, and a `display: none` element is not in it — so every locator for the chrome
 * would time out at print media, which is exactly when it has to be readable. Handles are captured
 * while the page is on screen media and stay valid across the switch.
 */
function css(handle: ElementHandle<Element>, prop: string): Promise<string> {
  return handle.evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), prop);
}

test.describe("CV print artifact", () => {
  // The operator's OS is dark. This is the default path, not an opt-in: P1T-159 made
  // `prefers-color-scheme` the default Theme Mode, so a dark-OS operator is here with no toggle.
  test.use({ colorScheme: "dark" });

  test.beforeEach(async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUp(page);
  });

  test("a CV printed from a dark-mode app is a light sheet with legible text", async ({ page }) => {
    // Dorothy Vaughan is this spec's own person. The suite runs `workers: 1` against one shared
    // roster and specs keep apart by owning the rows they create — a name another spec also uses
    // makes *its* row locator ambiguous, not this one's, so the collision surfaces somewhere else
    // entirely. Taken already: Ada Lovelace, Grace Hopper, Grace Murray, Barbara Liskov, Katherine
    // Johnson, Alan Turing.
    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill("Dorothy");
    await dialog.getByLabel("Last name").fill("Vaughan");
    await dialog.getByLabel("Title").fill("Research Mathematician");
    await dialog.getByLabel("Email").fill(uniqueEmail("print"));
    await dialog.getByLabel("Location").fill("Hampton");
    await dialog.getByLabel("Summary").fill("Computed trajectories by hand.");
    await dialog.getByRole("button", { name: "Save" }).click();

    await page.getByRole("row", { name: /Dorothy Vaughan/ }).getByTitle("View CV").click();
    await expect(page).toHaveURL(/\/cv$/);
    await expect(page.getByRole("heading", { name: "Dorothy Vaughan" })).toBeVisible();

    const body = (await page.locator("body").elementHandle())!;
    const sheet = (await page.locator("#cv-sheet").elementHandle())!;
    const heading = (await page
      .getByRole("heading", { name: "Dorothy Vaughan" })
      .elementHandle())!;
    const appBar = (await page.locator("header").elementHandle())!;
    const printButton = (await page
      .getByRole("button", { name: "Print", exact: true })
      .elementHandle())!;
    // The toolbar row, which is the element that actually carries the print rule — the buttons are
    // hidden by being inside it. Reached through the Back link rather than by class position, so it
    // survives P1T-162 moving this chrome into `PageHeader`.
    const toolbar = (await page
      .getByRole("link", { name: /back/i })
      .locator('xpath=ancestor::*[contains(@class,"MuiStack-root")][1]')
      .elementHandle())!;

    // The app around the sheet really is dark — otherwise nothing below means anything.
    expect(await css(body, "color")).toBe(DARK_TEXT);

    // On screen, already: the sheet is light inside a dark app. What the operator sees is what the
    // client gets, which is why this is a nested theme and not a print-only colour block.
    expect(await css(sheet, "background-color")).toBe(LIGHT.paper);
    expect(await css(sheet, "color")).toBe(LIGHT.text);
    expect(await css(heading, "color")).toBe(LIGHT.text);

    // And now at print media, where the artifact is actually made.
    await page.emulateMedia({ media: "print" });

    // Browsers drop background colours from print but keep `color`. That asymmetry is what made
    // the un-locked version dangerous rather than merely ugly: the dark paper would have vanished
    // and the near-white text would have survived onto white paper — a CV that prints blank. So the
    // load-bearing assertion is the text colour, twice, stated both ways.
    expect(await css(heading, "color")).toBe(LIGHT.text);
    expect(await css(heading, "color")).not.toBe(DARK_TEXT);
    expect(await css(sheet, "color")).toBe(LIGHT.text);
    expect(await css(body, "background-color")).toBe(LIGHT.paper);

    // The chrome is not part of the document, and Chromium agrees at print media — the P1T-154
    // `sx` print rules winning against MUI's own defaults, watched rather than read off a string.
    expect(await css(appBar, "display")).toBe("none");
    expect(await css(toolbar, "display")).toBe("none");

    // The sheet's elevation shadow is *polled*, and that is a finding rather than a flake workaround.
    // `MuiPaper` sets `transition: box-shadow`, so switching media animates the shadow away over
    // P1T-159's 150ms ceiling instead of dropping it: read immediately, this reports a mid-transition
    // interpolation (`0px 1.76281px 0.881405px -0.881405px …`), which is neither the elevation value
    // nor `none`. A real print paints after layout settles, so paper gets the flat sheet — but any
    // instant assertion here would have been asserting on an interpolation, and the emitted-CSS test
    // cannot see this at all.
    await expect.poll(() => css(sheet, "box-shadow"), { timeout: 5_000 }).toBe("none");

    // The print *button* is asserted on its layout box, not on its `display`. `display` does not
    // inherit, so a child of a `display: none` parent still computes its own value — this button
    // reports `flex` at print media while being entirely unrendered. Which is the point: the
    // question worth asking is "does it take space on the paper", and only a layout engine answers
    // that. An emitted-CSS check cannot even tell the two apart.
    expect(await printButton.boundingBox()).toBeNull();

    await page.emulateMedia({ media: null });

    // Back on screen the chrome returns. A print rule that had leaked to screen would have passed
    // every assertion above while breaking the page it was meant to leave alone.
    expect(await css(appBar, "display")).not.toBe("none");
    expect(await css(toolbar, "display")).not.toBe("none");
    expect(await printButton.boundingBox()).not.toBeNull();
  });
});
