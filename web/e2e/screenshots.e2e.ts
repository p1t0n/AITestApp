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
import type { Locator, Page } from "@playwright/test";
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
 * Wait for a moving thing to stop moving before shooting it.
 *
 * Every animated surface in this app lands inside the theme's 150ms motion ceiling, which is short
 * enough that an immediate capture looks like a settled frame and is not one: the first run of
 * P1T-161's two new states produced a "collapsed" rail 220px wide and a drawer halfway in from the
 * left. Same trap P1T-164 hit reading `box-shadow` an instant after a media switch — an
 * interpolation is indistinguishable from a broken rule. Two identical geometry reads is the
 * cheapest honest answer, and it needs no timeout to tune.
 */
async function settled(locator: Locator): Promise<void> {
  let previous = "";
  await expect
    .poll(async () => {
      const current = JSON.stringify(await locator.boundingBox());
      const stable = current === previous;
      previous = current;
      return stable;
    })
    .toBe(true);
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

/**
 * Filler rows, for the one shot whose subject is the *table* rather than a person: a roster has to
 * be longer than the viewport before a sticky header has anything to stick to. Seeded rather than
 * faked with a tiny viewport — a 200px-tall frame proves the behaviour (`page-header.e2e.ts` does
 * exactly that) but is useless as a picture of it.
 */
async function seedRoster(page: Page, mode: keyof typeof PEOPLE) {
  // Each mode takes its own half of the list. Both modes are captured in one run against one
  // database, and the dark pass runs second — sharing the names would put seven duplicate rows in
  // the very image whose subject is the table.
  const half = mode === "light" ? BENCH.slice(0, 7) : BENCH.slice(7);

  for (const [first, last] of half) {
    await page.getByRole("button", { name: "New CV" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("First name").fill(first);
    await dialog.getByLabel("Last name").fill(last);
    await dialog.getByLabel("Title").fill("Senior Engineer");
    await dialog.getByLabel("Email").fill(uniqueEmail("bench"));
    await dialog.getByLabel("Location").fill("Remote");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden();
  }
}

const BENCH = [
  ["Jean", "Bartik"],
  ["Frances", "Allen"],
  ["Adele", "Goldberg"],
  ["Sophie", "Wilson"],
  ["Lynn", "Conway"],
  ["Evelyn", "Granville"],
  ["Mary", "Keller"],
  ["Carol", "Shaw"],
  ["Erna", "Hoover"],
  ["Susan", "Kare"],
  ["Elizabeth", "Feinler"],
  ["Thelma", "Estrin"],
  ["Ida", "Rhodes"],
  ["Klara", "Dan"],
] as const;

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
      // The picker, not the dock/float control: the dock opens floating here, so `Float` does not
      // exist yet and `boundingBox` would auto-wait for an element that never arrives.
      await settled(page.getByRole("button", { name: /^Agent surface: / }));
      await shoot(page, mode, "7-dock-open");

      // The shell's two extra states, from P1T-161. Both are the rail rather than a page, so they
      // are shot over the roster: a rail with nothing beside it says nothing about the layout.
      await page.goto("/");
      await expect(page.getByRole("button", { name: "New CV" })).toBeVisible();
      await page.getByRole("button", { name: "Collapse the navigation rail" }).click();
      const expand = page.getByRole("button", { name: "Expand the navigation rail" });
      await expect(expand).toBeVisible();
      await settled(expand);
      await shoot(page, mode, "8-rail-collapsed");
      await page.getByRole("button", { name: "Expand the navigation rail" }).click();

      await page.setViewportSize({ width: 720, height: 900 });
      await page.getByRole("button", { name: "Open the navigation" }).click();
      const catalogLink = page.getByRole("link", { name: "Skill Catalog" });
      await expect(catalogLink).toBeVisible();
      await settled(catalogLink);
      await shoot(page, mode, "9-mobile-drawer");

      // Slice 4's own state: the heading strip pinned, with the border it only has when pinned.
      // Not `fullPage` — a full-page capture photographs the whole document and so unpins the
      // strip, leaving the one thing this image exists to show out of it.
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto("/");
      await expect(page.getByRole("heading", { level: 1, name: "CVs" })).toBeVisible();
      await seedRoster(page, mode);

      // Sized off the document rather than guessed. The two modes run against one database and the
      // dark pass runs second, so the roster is twice as long by then — a hard-coded height that
      // scrolls in dark does not scroll in light, which is how the first run of this failed.
      const scroll = 240;
      const docHeight = await page.evaluate(() => document.documentElement.scrollHeight);
      await page.setViewportSize({ width: 1440, height: Math.max(420, docHeight - scroll) });
      await page.mouse.wheel(0, scroll);
      const strip = page
        .locator("h1")
        .locator("xpath=ancestor::*[contains(@class,'MuiStack-root')][1]");
      await expect(strip).toHaveAttribute("data-pinned", "true");
      await settled(strip);
      fs.mkdirSync(path.join(OUT, mode), { recursive: true });
      await page.screenshot({ path: path.join(OUT, mode, "10-roster-scrolled.png") });

      // Slice 5's own states (P1T-163). The dock is this app's signature surface and one shot of
      // it open says nothing about the chrome: what changed is the header bar, the resize handle,
      // the bubbles and the ledger's own row, and each of those is a different state of the panel.
      // Numbered after the existing ten so a reader comparing slices keeps 1–10 meaning what they
      // meant in slices 1–4.
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto("/");
      await expect(page.getByRole("button", { name: "New CV" })).toBeVisible();
      const bubble = page.getByRole("button", { name: "Open the agents assistant" });
      await settled(bubble);
      await shoot(page, mode, "11-dock-closed");

      await bubble.click();
      const dockIt = page.getByRole("button", { name: "Dock to side" });
      if (await dockIt.count()) await dockIt.click();
      await expect(page.getByRole("button", { name: "Float" })).toBeVisible();

      // Mid-conversation, for the two bubbles this run *can* produce: the person's, and the error
      // turn. The e2e stack starts no Agents service (`run.mjs`), so the answer bubble is not
      // photographable here — and the error bubble is a designed state of this panel in its own
      // right, the one P1T-153 decided keeps a bubble's look rather than becoming a banner.
      await page.getByPlaceholder("Ask about the roster…").fill("Who knows React?");
      await page.getByRole("button", { name: "Send" }).click();
      await expect(page.getByText("Who knows React?")).toBeVisible();
      await settled(page.getByText("Who knows React?"));
      await shoot(page, mode, "12-dock-rosterqa");

      await page.getByRole("button", { name: /^Agent surface: / }).click();
      await page.getByRole("menuitem", { name: "Staffing" }).click();
      const picker = page.getByRole("button", { name: /^Agent surface: Staffing/ });
      await expect(picker).toBeVisible();
      // The menu fades out over the motion ceiling, and the first run of this shot photographed
      // the four group headers ghosting over the panel — the same 150ms trap as the rail. Waited
      // on the *popover*, not on `role="menu"`: MUI drops the role as soon as the menu closes and
      // keeps painting the paper for the length of the exit transition, so the role goes to zero
      // while the thing in the picture is still on screen.
      await expect(page.locator(".MuiPopover-root")).toHaveCount(0);
      await settled(picker);
      await shoot(page, mode, "13-dock-staffing");

      // Driven from the keyboard rather than by dragging: it is the affordance this slice added,
      // and using it to set up the picture is cheaper than a mouse gesture that has to be tuned.
      await page.getByRole("separator", { name: "Resize the agents dock" }).focus();
      await page.keyboard.press("Home");
      await expect
        .poll(() =>
          page.evaluate(() =>
            getComputedStyle(document.documentElement).getPropertyValue("--agent-dock-push").trim()))
        .toBe("360px");
      // Shot with the handle still focused, on purpose: the affordance is the subject.
      await shoot(page, mode, "14-dock-min-width");

      await page.getByRole("button", { name: "Token usage" }).click();
      await expect(page.getByRole("button", { name: /Back to Staffing/ })).toBeVisible();
      await shoot(page, mode, "15-dock-ledger");
    });
  });
}
