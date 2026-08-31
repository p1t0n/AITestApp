// The screenshot pass every slice of the P1T-158 design-system chain has to ship: the seven
// surfaces, in both Theme Modes. Committed rather than rebuilt from the ticket text six times —
// the same call the repo already makes for its gate harnesses (`CloudflareWorkersAiGateTests`,
// `CompatEndpointProbeTests`).
//
// Skipped in the default run because a screenshot is an artifact, not an assertion: nothing here
// can fail in a way that means the app is broken. Capture with `npm run shots`. It stays inside
// the default run's *compile* so it cannot rot between slices, which is the same reason
// `RosterScanGateChunkTests` exists.
//
// Dark mode is driven by Playwright's `colorScheme`, which sets `prefers-color-scheme` — so this
// also exercises the real default path in `src/theme/mode.ts` rather than a pinned override.
import fs from "node:fs";
import path from "node:path";
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp, uniqueEmail } from "./passkey";

const CAPTURE = process.env.E2E_SHOTS === "1";

// `docs/` is gitignored — the images are for a human's eyes on the day of the slice, and the
// record that outlives them is `manuals/spa-design-system.md`.
const OUT = path.resolve("..", "docs", "design-system-shots");

async function shoot(page: Page, mode: string, name: string) {
  fs.mkdirSync(path.join(OUT, mode), { recursive: true });
  await page.screenshot({ path: path.join(OUT, mode, `${name}.png`), fullPage: true });
}

/**
 * One employee per mode, with enough on it that the detail page and the CV are not empty frames.
 * A *different* person per mode on purpose: both modes are captured in one run against one
 * database, so a shared name would make the roster row locator ambiguous on the second pass.
 */
const PEOPLE = {
  light: { first: "Ada", last: "Lovelace", title: "Analytical Engineer", city: "London" },
  dark: { first: "Grace", last: "Hopper", title: "Systems Architect", city: "New York" },
} as const;

async function seedEmployee(page: Page, mode: keyof typeof PEOPLE): Promise<string> {
  const who = PEOPLE[mode];
  const fullName = `${who.first} ${who.last}`;

  await page.getByRole("button", { name: "New CV" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("First name").fill(who.first);
  await dialog.getByLabel("Last name").fill(who.last);
  await dialog.getByLabel("Title").fill(who.title);
  await dialog.getByLabel("Email").fill(uniqueEmail("shots"));
  await dialog.getByLabel("Location").fill(who.city);
  await dialog.getByLabel("Summary").fill("Wrote the first algorithm intended for a machine.");
  await dialog.getByRole("button", { name: "Save" }).click();

  const row = page.getByRole("row", { name: new RegExp(fullName) });
  await expect(row).toBeVisible();
  await row.getByRole("cell", { name: fullName }).click();
  await expect(page).toHaveURL(/\/employees\/[0-9a-f-]{36}$/);
  return page.url();
}

for (const mode of ["light", "dark"] as const) {
  test.describe(`${mode} mode screenshots`, () => {
    test.skip(!CAPTURE, "Artifact pass — run `npm run shots` to capture.");
    test.use({ colorScheme: mode });

    test(`captures the seven surfaces in ${mode}`, async ({ context, page }) => {
      // Sign-in first: it is the only one of the seven that must be shot with no session.
      await page.goto("/signin");
      await expect(page.getByRole("button", { name: /sign in with a passkey/i })).toBeVisible();
      await shoot(page, mode, "1-signin");

      await addVirtualAuthenticator(context, page);
      await signUp(page);

      const detailUrl = await seedEmployee(page, mode);

      await page.goto("/");
      await expect(page.getByRole("button", { name: "New CV" })).toBeVisible();
      await shoot(page, mode, "2-roster");

      const fullName = `${PEOPLE[mode].first} ${PEOPLE[mode].last}`;
      await page.goto(detailUrl);
      await expect(page.getByRole("heading", { name: fullName })).toBeVisible();
      await shoot(page, mode, "3-employee-detail");

      await page.goto(`${detailUrl}/cv`);
      await expect(page.getByRole("heading", { name: fullName })).toBeVisible();
      await shoot(page, mode, "4-cv-page");

      // Both of these load their content *after* the frame renders, and neither had a wait: the
      // light catalog capture in slice 1 and slice 2 is a spinner on an empty page, which is a
      // screenshot of nothing. Asserting the loading state is gone rather than naming seeded rows
      // keeps the capture independent of what the e2e database happens to hold.
      await page.goto("/catalog");
      await expect(page.getByRole("heading", { name: "Skill Catalog" })).toBeVisible();
      await expect(page.getByRole("progressbar")).toHaveCount(0);
      await shoot(page, mode, "5-catalog");

      await page.goto("/users");
      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
      await expect(page.getByText("Loading…")).toHaveCount(0);
      await shoot(page, mode, "6-users");

      // The dock last, over the roster: it is the app's signature surface and needs a page under
      // it to read as a dock rather than as a panel.
      await page.goto("/");
      await page.getByRole("button", { name: "Open the agents assistant" }).click();
      await shoot(page, mode, "7-dock-open");
    });
  });
}
