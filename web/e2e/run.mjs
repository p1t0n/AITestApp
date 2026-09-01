// Entry point for the e2e suite: owns the whole stack for one run.
//
// Why a script instead of Playwright's own `webServer`: the API cannot start until a database
// exists, and the database is a container this run creates. Sequencing that here keeps the
// ordering explicit — container, then API, then Playwright (which starts the SPA itself) — and
// guarantees the container is removed on every exit path, including Ctrl-C.
//
// Nothing here touches the dev stack: its own ports, its own container, its own database.
import { spawn, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");

export const PORTS = { db: 55433, api: 5079, spa: 5174 };
const CONTAINER = "experttojob-e2e-db";
const IMAGE = "pgvector/pgvector:pg17";

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", ...options });
  if (result.error) throw result.error;
  return result;
}

function removeContainer() {
  run("docker", ["rm", "-f", CONTAINER], { stdio: "ignore" });
}

async function startDatabase() {
  // A leftover from an interrupted run would hold the port; start from a clean slate.
  removeContainer();

  const started = run("docker", [
    "run", "-d", "--rm",
    "--name", CONTAINER,
    "-e", "POSTGRES_USER=postgres",
    "-e", "POSTGRES_PASSWORD=postgres",
    "-e", "POSTGRES_DB=experttojob_e2e",
    "-p", `${PORTS.db}:5432`,
    IMAGE,
  ]);
  if (started.status !== 0) {
    throw new Error(`Could not start the e2e database container. Is Docker running?\n${started.stderr}`);
  }

  await waitFor("postgres", 60_000, () =>
    run("docker", ["exec", CONTAINER, "pg_isready", "-U", "postgres", "-d", "experttojob_e2e"])
      .status === 0);
}

async function waitFor(what, timeoutMs, isReady) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    if (await isReady()) return;
    if (Date.now() > deadline) throw new Error(`Timed out waiting for ${what}.`);
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
}

async function startApi() {
  const api = spawn("dotnet", [
    "run", "--project", "api/Web", "--no-launch-profile",
  ], {
    cwd: repoRoot,
    stdio: process.env.E2E_VERBOSE ? "inherit" : "ignore",
    env: {
      ...process.env,
      // Development is the environment that applies migrations and the seed on startup.
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: `http://localhost:${PORTS.api}`,
      ConnectionStrings__Default:
        `Host=localhost;Port=${PORTS.db};Database=experttojob_e2e;Username=postgres;Password=postgres`,
      // The passkey relying party checks the browser's origin against this list, and the suite
      // serves the SPA on its own port.
      Auth__Passkey__Origins__0: `http://localhost:${PORTS.spa}`,
    },
  });

  await waitFor(`the Web API on :${PORTS.api}`, 120_000, async () => {
    if (api.exitCode !== null) throw new Error(`The Web API exited early (code ${api.exitCode}).`);
    try {
      // Anything that answers means the host is up; /api/experts 401s without a token.
      const response = await fetch(`http://localhost:${PORTS.api}/api/experts`);
      return response.status === 401 || response.ok;
    } catch {
      return false;
    }
  });

  return api;
}

async function main() {
  let api;
  const shutdown = () => {
    api?.kill("SIGTERM");
    removeContainer();
  };
  process.on("SIGINT", () => { shutdown(); process.exit(130); });
  process.on("SIGTERM", () => { shutdown(); process.exit(143); });

  try {
    await startDatabase();
    api = await startApi();

    const playwright = spawn(
      "npx",
      ["playwright", "test", ...process.argv.slice(2)],
      { stdio: "inherit", env: { ...process.env, E2E_BASE_URL: `http://localhost:${PORTS.spa}` } },
    );
    const code = await new Promise((resolve) => playwright.on("close", resolve));
    process.exitCode = code ?? 1;
  } catch (error) {
    console.error(`\ne2e stack failed to start: ${error.message}\n`);
    process.exitCode = 1;
  } finally {
    shutdown();
  }
}

await main();
