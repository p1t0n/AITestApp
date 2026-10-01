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
import fs from "node:fs";
import path from "node:path";
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
    vi.stubEnv("E2E_AGENTS_PORT", "6003");
    vi.stubEnv("E2E_BASE_URL", "");

    const config = await playwrightConfig();

    expect(config.use?.baseURL).toBe("http://localhost:6001");
    expect(config.webServer).toMatchObject({
      env: {
        VITE_PORT: "6001",
        VITE_API_TARGET: "http://localhost:6002",
        VITE_AGENTS_TARGET: "http://localhost:6003",
      },
    });
  });

  it.each(["E2E_SPA_PORT", "E2E_API_PORT", "E2E_AGENTS_PORT"])(
    "refuses to guess when the harness did not set %s",
    async (missing) => {
      for (const name of ["E2E_SPA_PORT", "E2E_API_PORT", "E2E_AGENTS_PORT"]) {
        vi.stubEnv(name, name === missing ? "" : "6002");
      }

      await expect(playwrightConfig()).rejects.toThrow(missing);
    },
  );

  // The agents target is the one that has to be *set* rather than merely consistent (EXP-91).
  // Unset, `vite.config.ts` falls back to `:5200` — the dev Agents host — which answers 401 to a
  // session minted against the e2e database, and `src/api/http.ts` ends the session on any 401. So
  // the suite went red for anybody with a dev stack up and stayed green on CI, where nothing
  // listens there. Read out of `run.mjs`'s source because importing it would start the stack.
  it("hands over an agents port that is not the dev Agents host", () => {
    const harness = fs.readFileSync(
      path.join(import.meta.dirname, "..", "e2e", "run.mjs"),
      "utf8",
    );

    const declared = harness.match(/^const PORTS = \{(.+)\};$/m)?.[1];
    const agents = Number(declared?.match(/\bagents:\s*(\d+)/)?.[1]);

    expect(agents, "run.mjs no longer decides an agents port").toBeGreaterThan(0);
    expect(agents, "the e2e suite would proxy /agents at the dev Agents host").not.toBe(5200);
    expect(harness, "the agents port is decided but never handed to Playwright").toContain(
      "E2E_AGENTS_PORT: String(PORTS.agents)",
    );
  });
});
