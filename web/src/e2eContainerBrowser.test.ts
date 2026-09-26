// @vitest-environment node
//
// The container-browser switch (EXP-38). `E2E_CONTAINER_BROWSER=1 npm run test:e2e` runs the
// functional suite against the pinned Playwright image instead of a browser Playwright downloads —
// the one step an egress-restricted sandbox cannot do. Three files have to agree for that to work:
// `e2e/run.mjs` starts the container and hands over its websocket, `playwright.config.ts` connects
// the `chromium` project to it, and `vite.config.ts` binds the SPA where the container can reach
// it. The last two are configuration, so they are asserted here as configuration; the first is
// proved by running the suite that way.
import { afterEach, describe, expect, it, vi } from "vitest";

async function playwrightConfig() {
  vi.resetModules();
  return (await import("../playwright.config")).default;
}

async function viteServerHost() {
  vi.resetModules();
  const config = (await import("../vite.config")).default as { server?: { host?: unknown } };
  return config.server?.host;
}

const chromiumOf = (config: Awaited<ReturnType<typeof playwrightConfig>>) =>
  config.projects?.find((p) => p.name === "chromium");

afterEach(() => vi.unstubAllEnvs());

describe("the container-browser switch", () => {
  it("launches a local browser for the functional suite when no container browser is running", async () => {
    vi.stubEnv("E2E_BROWSER_WS", "");
    expect(chromiumOf(await playwrightConfig())?.use?.connectOptions).toBeUndefined();
  });

  it("connects the functional suite to the container browser when run.mjs hands one over", async () => {
    vi.stubEnv("E2E_BROWSER_WS", "ws://localhost:5175/");
    expect(chromiumOf(await playwrightConfig())?.use?.connectOptions).toEqual({
      wsEndpoint: "ws://localhost:5175/",
    });
  });

  it("binds the SPA beyond loopback for a container-browser run, and only then", async () => {
    vi.stubEnv("E2E_VISUAL", "");
    vi.stubEnv("E2E_CONTAINER_BROWSER", "");
    expect(await viteServerHost()).toBe(false);

    vi.stubEnv("E2E_CONTAINER_BROWSER", "1");
    expect(await viteServerHost()).toBe(true);
  });
});
