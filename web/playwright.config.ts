import { defineConfig, devices } from "@playwright/test";

// The database and the Web API are started by `web/e2e/run.mjs` before Playwright runs — the API
// cannot boot without a database, and Playwright's webServer has no way to express that order.
// The SPA has no such dependency, so it is started here, on its own port against the e2e API.
const SPA_PORT = 5174;
const API_PORT = 5079;
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${SPA_PORT}`;

// The visual pass renders in the pinned Playwright container (`e2e/run.mjs` starts it, and says
// why). Both variables come from there: without them the `visual` project is skipped, so the
// ordinary suite needs no Docker image and no baseline ever gets written by a host browser.
const browserWs = process.env.E2E_BROWSER_WS;
const VISUAL_MATCH = /.*\.visual\.e2e\.ts/;

export default defineConfig({
  testDir: "./e2e",
  testMatch: /.*\.e2e\.ts/,
  // The suite shares one roster; specs keep apart by owning the rows and accounts they create.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],
  timeout: 30_000,

  use: {
    baseURL,
    trace: "retain-on-failure",
    video: "off",
  },

  // One directory for the baselines, with no platform or project suffix: there is exactly one
  // environment that may produce them, so a second directory could only ever hold images nothing
  // compares against. `{arg}` is the snapshot name the spec passes.
  snapshotPathTemplate: "{testDir}/__screenshots__/{arg}{ext}",

  expect: {
    timeout: 10_000,
    toHaveScreenshot: {
      // Tight, and measured rather than guessed. The first cut used `maxDiffPixelRatio: 0.002`,
      // which sounds small and is 2,592 pixels on a 1440×900 frame — enough to swallow a card
      // radius moving 14px to 20px, and that is the class of change this net exists to catch: at
      // 40 pixels the same edit fails at 59-68 differing pixels. Repeated runs against the pinned
      // browser differ by zero, so the 12 left here is a hedge against emulation jitter rather
      // than a real allowance.
      maxDiffPixels: 12,
      animations: "disabled",
      scale: "css",
    },
  },

  projects: [
    {
      name: "chromium",
      // Chromium only, and not by preference: the virtual authenticator that lets these tests
      // complete a passkey ceremony headlessly is a Chrome DevTools Protocol feature.
      use: { ...devices["Desktop Chrome"] },
      testIgnore: VISUAL_MATCH,
    },
    {
      // The visual net (P1T-198). Separate because it is the one project whose result depends on
      // *rasterisation*: it runs against the pinned container browser or it does not run at all.
      name: "visual",
      testMatch: VISUAL_MATCH,
      use: {
        ...devices["Desktop Chrome"],
        // A baseline is only comparable at a fixed frame.
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 1,
        connectOptions: browserWs ? { wsEndpoint: browserWs } : undefined,
      },
    },
  ],

  webServer: {
    command: "npm run dev",
    url: baseURL,
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: "ignore",
    stderr: "pipe",
    env: {
      VITE_PORT: String(SPA_PORT),
      VITE_API_TARGET: `http://localhost:${API_PORT}`,
    },
  },
});
