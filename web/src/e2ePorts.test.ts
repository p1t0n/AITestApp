// @vitest-environment node
//
// Where the e2e ports come from (EXP-83). `e2e/run.mjs` owns the e2e stack — it starts the
// database, the migrator and the API, and it is the only thing that ever starts Playwright — so
// its `PORTS` is the one place the numbers are decided. `playwright.config.ts` used to repeat two
// of them as literals, which is the kind of duplication that stays correct right up until someone
// moves one of them.
//
// So the config reads them, and reads them with no fallback: a default here would be the same
// duplicate wearing a `??`. Asserted as configuration, the way the container-browser switch is.
import { afterEach, describe, expect, it, vi } from "vitest";

async function playwrightConfig() {
  vi.resetModules();
  return (await import("../playwright.config")).default;
}

afterEach(() => vi.unstubAllEnvs());

describe("the e2e ports", () => {
  it("come from the environment run.mjs hands over, not from a second copy of the numbers", async () => {
    vi.stubEnv("E2E_SPA_PORT", "6001");
    vi.stubEnv("E2E_API_PORT", "6002");
    vi.stubEnv("E2E_BASE_URL", "");

    const config = await playwrightConfig();

    expect(config.use?.baseURL).toBe("http://localhost:6001");
    expect(config.webServer).toMatchObject({
      env: { VITE_PORT: "6001", VITE_API_TARGET: "http://localhost:6002" },
    });
  });

  it("refuses to guess when the harness did not set them", async () => {
    vi.stubEnv("E2E_SPA_PORT", "");
    vi.stubEnv("E2E_API_PORT", "6002");

    await expect(playwrightConfig()).rejects.toThrow("E2E_SPA_PORT");
  });
});
