// The visual net (P1T-198). The first thing in this repo that can fail because the app *looks*
// wrong.
//
// Until now the look had no regression net at all: `screenshots.e2e.ts` captures PNGs behind
// `E2E_SHOTS=1` for a human to look at, and there was no `toHaveScreenshot` anywhere, so a
// re-skin as total as the neumorphic reversal was checked by eye. The token suite proves the
// colours are the colours and `theme/components.test.tsx` proves the overrides land, but neither
// can see a shadow rendered on the wrong side of a card.
//
// **Narrow on purpose.** Two frames per mode — the shell, and the roster card inside it. Full
// baselines turn every spacing change into a baseline-update chore, which is how a visual suite
// stops being read and starts being `--update-snapshots`-ed. Everything else leans on the token
// and computed-style suites, which fail with a number rather than a picture.
//
// **Pinned browser or nothing.** These run only against the containerised Playwright at the exact
// pinned version (`e2e/run.mjs` starts it and says why); the `visual` project skips otherwise. A
// baseline captured on a developer's Mac and compared on a CI runner is a coin toss — three
// self-hosted font families made that worse, not better.
//
// **What is masked, and why it is not cheating.** Two regions carry values that are not a property
// of the design: the signed-in account's email (a fresh unique address every run) and the
// "Availability (today)" column (a function of the calendar — the seeded schedules step through
// 2026 and 2027, which is exactly the time bomb P1T-199 had to defuse in the cost floor). Masking
// keeps the cells in frame, at their real size, with their real chrome; only their text is out of
// the comparison. A baseline that photographed either would be red on a date nobody chose.
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

/**
 * Everything that has to be true before a frame is worth comparing: the three families actually
 * loaded (a fallback stack renders at a different width and every glyph moves), and no spinner
 * still on screen.
 */
async function readyToShoot(page: Page) {
  await expect(page.getByRole("progressbar")).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
}

for (const mode of ["light", "dark"] as const) {
  test.describe(`${mode} mode`, () => {
    test.skip(
      !process.env.E2E_BROWSER_WS,
      "Renders in the pinned Playwright container — run `npm run test:visual`.",
    );
    test.use({ colorScheme: mode });

    test(`the shell and the roster hold their design in ${mode}`, async ({ context, page }) => {
      await page.goto("/signin");
      await addVirtualAuthenticator(context, page);
      await signUp(page);

      // Nothing is seeded here on purpose. The roster the baseline photographs is the API's own
      // startup seed — three fixed people — so both modes shoot the same table and neither depends
      // on the other having run. A spec that seeded its own rows would put six rows in the second
      // frame and three in the first, and the difference would look like a design change.
      await page.goto("/");
      await expect(page.getByRole("heading", { level: 1, name: "CVs" })).toBeVisible();
      await readyToShoot(page);

      // The account's own address, which is a different string on every run.
      const email = page.getByText(/@/).first();
      // The column whose value is the date, not the design.
      const availability = page.locator("td:nth-child(4)");

      // The shell: rail, brand tile, page header, and the roster under it — the frame the whole
      // re-skin was about. Viewport rather than full page: the strip's pinned state is part of the
      // subject, and a full-page capture unpins it.
      await expect(page).toHaveScreenshot(`shell-${mode}.png`, {
        mask: [email, availability],
      });

      // And the roster card on its own, larger in the frame: the Eyebrow header row, the hairline
      // rules, the row rhythm. An element shot clips the card's own relief — that lives in the
      // shell frame above, where the shadow has ground to fall on.
      // Walked out from the first column header, whose name is frozen (§9) — the card carries no
      // test id and its class nesting is exactly the kind of thing this suite exists to let move.
      const rosterCard = page
        .getByRole("columnheader", { name: "Name" })
        .locator('xpath=ancestor::*[contains(@class,"MuiPaper-root")][1]');

      await expect(rosterCard).toHaveScreenshot(`roster-${mode}.png`, { mask: [availability] });
    });
  });
}
