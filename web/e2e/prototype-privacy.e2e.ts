// PROTOTYPE DRIVER — throwaway, pairs with `src/pages/prototype/` (P1T-175).
//
// Signs up with a virtual authenticator, then drives /prototype/privacy through each variant and
// the state combinations the ticket was actually worried about, shooting each one. No assertions
// beyond "the page rendered": a screenshot is an artifact, not a test.
//
//   cd web && E2E_SHOTS=1 node e2e/run.mjs e2e/prototype-privacy.e2e.ts
import fs from "node:fs";
import path from "node:path";
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

// `docs/` is gitignored — these are for a human's eyes on the day, like the design-system shots.
const OUT = path.resolve("..", "docs", "prototype-privacy");

const VARIANTS = ["A", "B", "C"] as const;

async function shoot(page: Page, name: string) {
  fs.mkdirSync(OUT, { recursive: true });
  // The switcher is `position: fixed`, so in a full-page capture it lands mid-document and covers
  // whatever happens to be there. Hide it for the shot only — a persistent style tag would also
  // stop the next `flip()` from being able to click it.
  const toggle = (display: string) =>
    page.evaluate((d) => {
      const el = document.querySelector<HTMLElement>("[data-prototype-switcher]");
      if (el) el.style.display = d;
    }, display);

  await toggle("none");
  await page.screenshot({ path: path.join(OUT, `${name}.png`), fullPage: true });
  await toggle("");
}

/** The switcher's state-flipping buttons are plain text buttons; click by current label. */
async function flip(page: Page, label: string) {
  await page.getByRole("button", { name: label, exact: false }).first().click();
}

async function open(page: Page, variant: string) {
  await page.goto(`/prototype/privacy?variant=${variant}`);
  await expect(page.getByRole("heading", { name: "Privacy and data" })).toBeVisible();
  // Let the 150ms motion ceiling settle before capturing.
  await page.waitForTimeout(300);
}

test("drive the three Privacy & data variants through the coinciding states", async ({
  context,
  page,
}) => {
  await addVirtualAuthenticator(context, page);
  await signUp(page);

  for (const v of VARIANTS) {
    // 1. Baseline: active, contract basis, not expiring.
    await open(page, v);
    await shoot(page, `${v}-1-active-contract`);

    // 2. Paused.
    await flip(page, "active");
    await page.waitForTimeout(250);
    await shoot(page, `${v}-2-paused`);

    // 3. Paused AND expiring — two warnings competing for one slot.
    await flip(page, "not expiring");
    await page.waitForTimeout(250);
    await shoot(page, `${v}-3-paused-and-expiring`);

    // 4. Legitimate interest: an Object control materialises, export becomes a courtesy,
    //    and the profile stops being matched.
    await flip(page, "paused");
    await flip(page, "6(1)(b) contract");
    await page.waitForTimeout(250);
    await shoot(page, `${v}-4-legitimate-interest`);

    // 5. Claim pending: no CV at all, so most of the page has nothing to say.
    await flip(page, "owns row");
    await page.waitForTimeout(250);
    await shoot(page, `${v}-5-claim-pending`);
  }

  // One dark-mode pass on the baseline, since the app has two Theme Modes and a prototype that
  // only works in one is not finished.
  await page.emulateMedia({ colorScheme: "dark" });
  for (const v of VARIANTS) {
    await open(page, v);
    await shoot(page, `${v}-6-dark`);
  }
});
