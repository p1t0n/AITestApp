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

export const PORTS = { db: 55433, api: 5079, spa: 5174, browser: 5175 };
const CONTAINER = "experttojob-e2e-db";
const IMAGE = "pgvector/pgvector:pg17";

/**
 * The visual pass (P1T-198) renders in a **pinned browser**, not in whatever Chromium the host
 * happens to have: a screenshot baseline is a promise about rasterisation, and font hinting,
 * subpixel geometry and the shipped font stack all differ between a developer's Mac and a CI
 * runner. Three self-hosted families made that gap wider, not narrower.
 *
 * So the browser runs inside the official Playwright image at the exact version this repo pins,
 * and Playwright connects to it over a websocket while the *app* stays on the host. One rendering
 * environment for everybody — which is what makes a committed baseline mean something, and what
 * lets a developer regenerate one without waiting for CI to tell them what it looks like.
 */
const VISUAL = process.env.E2E_VISUAL === "1";
const BROWSER_CONTAINER = "experttojob-e2e-browser";
const BROWSER_IMAGE = "mcr.microsoft.com/playwright:v1.62.1-noble";
/** How the container reaches the host it is running on. */
const HOST_FROM_CONTAINER = "host.docker.internal";

/**
 * The container forwards its own `localhost:5174` to the SPA on the host, and the browser is
 * pointed at `http://localhost:5174` like every other spec in this suite. Two reasons it has to be
 * `localhost` rather than the host alias:
 *
 *   * **WebAuthn needs a secure context.** `http://host.docker.internal:5174` is not one, so
 *     `window.PublicKeyCredential` does not exist, the signup button stays disabled, and no visual
 *     spec can get past the front door. `localhost` is trusted by the browser without TLS.
 *   * **The passkey relying party checks the origin**, so borrowing `localhost` also means the
 *     visual pass needs no second origin on the API and no second code path to keep true.
 */
const FORWARD_TO_HOST =
  "const net=require('net');" +
  `net.createServer(c=>{const u=net.connect(${PORTS.spa},'${HOST_FROM_CONTAINER}');` +
  "c.pipe(u);u.pipe(c);u.on('error',()=>c.destroy());c.on('error',()=>u.destroy());})" +
  `.listen(${PORTS.spa},'127.0.0.1');`;

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", ...options });
  if (result.error) throw result.error;
  return result;
}

function removeContainer() {
  run("docker", ["rm", "-f", CONTAINER], { stdio: "ignore" });
}

function removeBrowserContainer() {
  run("docker", ["rm", "-f", BROWSER_CONTAINER], { stdio: "ignore" });
}

async function startBrowserServer() {
  removeBrowserContainer();

  const started = run("docker", [
    "run", "-d", "--rm",
    "--name", BROWSER_CONTAINER,
    // One architecture for every baseline. CI runs on amd64 and a developer's Mac is arm64, and
    // the image is multi-arch — so without this the two would render with different Chromium
    // builds and a baseline committed from a laptop would be red the moment CI looked at it.
    // Emulated here, which costs a few seconds and buys the only thing a baseline is worth.
    "--platform", "linux/amd64",
    // Chromium's default 64MB /dev/shm makes a headless tab die under a full-page screenshot.
    "--ipc=host",
    // Linux runners have no `host.docker.internal`; Docker Desktop provides it already, and
    // declaring it twice is harmless.
    "--add-host", `${HOST_FROM_CONTAINER}:host-gateway`,
    "-p", `${PORTS.browser}:${PORTS.browser}`,
    // The image ships the browsers but no `playwright` on its PATH, so the server is started from
    // this repo's own copy — mounted read-only, because the container has no business writing
    // here. Same package version by construction, which is the version the image is tagged with.
    "-v", `${path.join(repoRoot, "web")}:/work:ro`,
    "-w", "/work",
    BROWSER_IMAGE,
    "sh", "-c",
    `node -e "${FORWARD_TO_HOST}" & ` +
    `exec node node_modules/playwright-core/cli.js run-server --port ${PORTS.browser} --host 0.0.0.0`,
  ]);
  if (started.status !== 0) {
    throw new Error(`Could not start the pinned browser container. Is Docker running?\n${started.stderr}`);
  }

  await waitFor(`the pinned browser on :${PORTS.browser}`, 120_000, async () => {
    try {
      // `run-server` speaks websocket and answers a plain GET with an error — which is still an
      // answer. Anything but a refused connection means it is listening.
      await fetch(`http://localhost:${PORTS.browser}/`);
      return true;
    } catch {
      return false;
    }
  });
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
      // serves the SPA on its own port. The visual pass borrows the same origin — see
      // `FORWARD_TO_HOST` for why its containerised browser also says `localhost`.
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
    removeBrowserContainer();
  };
  process.on("SIGINT", () => { shutdown(); process.exit(130); });
  process.on("SIGTERM", () => { shutdown(); process.exit(143); });

  try {
    await startDatabase();
    api = await startApi();
    if (VISUAL) await startBrowserServer();

    const playwright = spawn(
      "npx",
      ["playwright", "test", ...process.argv.slice(2)],
      {
        stdio: "inherit",
        env: {
          ...process.env,
          E2E_BASE_URL: `http://localhost:${PORTS.spa}`,
          // Read by the `visual` project in `playwright.config.ts`. Only set for a visual run, so
          // the ordinary suite keeps launching a local browser and needs no Docker image.
          ...(VISUAL ? { E2E_BROWSER_WS: `ws://localhost:${PORTS.browser}/` } : {}),
        },
      },
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
