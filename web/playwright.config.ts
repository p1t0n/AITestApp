import { defineConfig, devices } from "@playwright/test";

// The database and the Web API are started by `web/e2e/run.mjs` before Playwright runs — the API
// cannot boot without a database, and Playwright's webServer has no way to express that order.
// The SPA has no such dependency, so it is started here, on its own port against the e2e API.
const SPA_PORT = 5174;
const API_PORT = 5079;
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${SPA_PORT}`;

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
  expect: { timeout: 10_000 },

  use: {
    baseURL,
    trace: "retain-on-failure",
    video: "off",
  },

  projects: [
    {
      name: "chromium",
      // Chromium only, and not by preference: the virtual authenticator that lets these tests
      // complete a passkey ceremony headlessly is a Chrome DevTools Protocol feature.
      use: { ...devices["Desktop Chrome"] },
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
