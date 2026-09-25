# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Backend (.NET 10, solution `ExpertToJob.slnx`):

```bash
dotnet build
dotnet test                                    # unit + Testcontainers integration (needs Docker)
dotnet test --filter "Category!=e2e&Category!=live"   # what CI and the Ralph loop run
dotnet test tests/Application.Tests             # one project
dotnet test --filter "FullyQualifiedName~CvAssemblerTests"   # one class/method
dotnet test --filter "Category=live"           # real model/embeddings; needs a provider key
dotnet test --filter "Category=eval"           # tool-selection gate, ~3 min, 39 model calls
```

`Category=live` is no longer one provider: each live test skips on its own missing key rather than
failing, so the set that actually runs depends on what is exported. `GEMINI_API_KEY` for the
incumbent path and the eval gate; `AZURE_FOUNDRY_API_KEY` for the Azure dialect probe
(`az cognitiveservices account keys list -n experttojob-openai-swc -g rg-experttojob-foundry
--query key1 -o tsv`); `CLOUDFLARE_ACCOUNT_ID` + `CLOUDFLARE_API_TOKEN` for the Workers AI gate.
A live probe that measures a provider is committed **without** that provider's shims, so a shim
that turns out to be needed shows up as a red test rather than as a surprise in the build.

Frontend (`web/`):

```bash
npm test                        # vitest, run mode
npm test -- src/theme/tokens.contrast.test.ts   # one file
npm test -- -t "renders the rail"               # one test by name
npm run typecheck               # tsc --noEmit
npm run lint                    # eslint
npm run dev                     # Vite on :5173, proxies /api and /agents
npm run test:e2e                # Playwright; owns its own DB container + API + SPA
npm run test:e2e -- e2e/shell.e2e.ts            # one spec
npm run shots                   # capture screenshots (E2E_SHOTS=1)
npm run test:visual             # shell visual regression, `visual` Playwright project
npm run visual:update           # re-baseline it — only with the diff looked at
```

Before committing SPA changes run all four: `npm test`, `npm run typecheck`, `npm run lint`, and
the backend suite if anything under `api/` moved.

Migrations:

```bash
dotnet ef migrations add <Name> \
  --project api/Infrastructure/ExpertToJob.Infrastructure.csproj \
  --startup-project api/Web/ExpertToJob.Web.csproj \
  --output-dir Persistence/Migrations
```

## Running the stack

One command, with Docker running — the Aspire AppHost owns the whole local stack:

```bash
dotnet run --project api/AppHost
```

| Resource | What it is | Port |
|---|---|---|
| `postgres` | PostgreSQL 17 + pgvector, persistent volume `aitestapp_experttojob-pgdata` | 5432 |
| `keycloak` | Authorization Server; the `expert-to-job` realm is imported on every start | 8080 |
| `migrator` | one-shot: EF migrations + base seed, then exits; the three hosts wait on it | — |
| `experttojob-web` | Web API (Swagger at `/swagger`) | 5069 |
| `experttojob-mcp` | MCP server | 5100 |
| `experttojob-agents` | Agents host | 5200 |
| `spa` | Vite dev server, proxies `/api` and `/agents` | 5173 |
| `demo-roster` | 500 synthetic experts — explicit start, never runs with the stack | — |

The AppHost prints the dashboard URL on startup; that is where the logs, traces and metrics are,
and where an explicit-start resource is started. Every port above is pinned, so every checked-in
literal (`vite.config.ts`, the `appsettings.json` files, the realm export) stays true —
`manuals/adr-aspire-apphost.md` says why, and lists the tripwires that silently do the wrong thing.

The one optional secret is the Gemini key, and only to stop the agents degrading:
`dotnet user-secrets set Parameters:gemini-api-key <key> --project api/AppHost`. The chat backend
itself is configuration: `Ai:Chat:Provider` names the provider and `Ai:Gemini:*` holds its
settings. A leftover top-level `Gemini` section throws at startup rather than binding to nothing —
`ConfigKeyMigrationTests` holds both halves of that rule.

A solo `dotnet run` in one project still works — each keeps its own launch profile — but **no host
applies migrations any more**, so against a fresh database run `dotnet run --project api/Migrator`
first. `dotnet run --project tools/SeedDemoRoster` adds the 500 synthetic experts outside the
AppHost (`--wipe` removes exactly those rows).

**Azure access is scoped by `.mcp.json`**, not by whoever ran `az login`. The tracked file pins a
project-scoped `azure` MCP server to `AZURE_TOKEN_CREDENTIALS=env`, so it resolves the service
principal whose role assignment covers `rg-experttojob-foundry` and nothing else. Every value in it
is an env-var reference; the credential itself lives outside the repo. No `AZURE_*` exported means
the server starts and fails to authenticate — which is the intended failure, not a reason to fall
back to a machine-wide login.

## Architecture

Five processes: Web API · MCP server · Agents · SPA · (Postgres + Keycloak).

**The Application layer is the single behaviour seam.** `api/Application` holds services, DTOs,
FluentValidation validators and CV assembly. Both `api/Web` (REST controllers) and `api/Mcp` (tool
adapters) are thin shells over it, which is why REST and MCP validate identically. A rule added in
a controller instead of the Application layer silently does not apply to MCP.

**The model reaches the roster only through MCP.** Every roster fact an agent reasons over comes
from the MCP server over Streamable HTTP, holding a Keycloak client-credentials token whose scope
decides which tools it is even shown — capability is enforced by the token, not by the prompt. The
Agents *host* is not database-free: it references Application and Infrastructure and keeps its own
records through `IAppDbContext` (usage, staffing proposals, scoring jobs, and Roster Q&A
conversations per `manuals/adr-roster-qa-conversation-history.md`), applying `RosterVisibility`
where those records touch Experts. Deterministic facts (an expert's data, counts, stats) come from
captured MCP results and are composed in code; the model writes prose. Orchestration composes the
single-agent runs and degrades a failed stage rather than failing the call. `IChatClient` keeps the
provider swappable (Gemini free tier by default).

**Two backends, one token.** The SPA talks to `/api/*` (5069) and `/agents/*` (5200) through two
axios instances (`web/src/api/http.ts`) that attach the same bearer. The Web host issues the
session JWT and both hosts validate it with a shared signing key. Vite proxies both in dev;
`VITE_API_TARGET` / `VITE_AGENTS_TARGET` retarget them for e2e.

**The SPA has no React Context.** Session and theme mode are hand-rolled `useSyncExternalStore`
subscriptions (`web/src/auth/session.ts`, `web/src/theme/mode.ts`). Server state is TanStack Query.

**Styling goes through the theme, not the component.** MUI 9 with a token layer in
`web/src/theme/` (`tokens.ts` → `index.ts` builds both themes → `components.ts` overrides →
`baseline.ts` floors). Components name MUI palette roles (`background.paper`, `divider`,
`text.secondary`), never a token. The **Override Policy**: a look needed twice belongs in
`components.ts`; `sx` is for spacing and layout only. `web/src/index.css` is deliberately 10 lines.

**Passkeys only** — WebAuthn sign-up/sign-in, no passwords. E2E drives real ceremonies against a
CDP virtual authenticator.

## Conventions

**Docs.** `CONTEXT.md` is the domain glossary and nothing else — no implementation detail, no spec.
Tracked technical docs and decision records go in `manuals/`; **`docs/` is gitignored**. `SPEC.md`
describes the original POC and has drifted (e.g. it still names GitHub Models as the chat backend);
`README.md` and `manuals/` are current. `manuals/spa-architecture.md` and
`manuals/spa-design-system.md` are the governing specs for the SPA — read them before changing the
shell, the theme, or anything they name as frozen.

**Package versions are central.** `Directory.Packages.props` at the repo root holds every version
once; a `PackageReference` names a package and never a `Version=` (P1T-222). Adding a package means
two edits — the reference in the csproj, and a `PackageVersion` entry — and a version that appears
in a csproj is a build error, not a preference. The one pin this does not cover is the AppHost's
`Sdk="Aspire.AppHost.Sdk/<version>"` attribute, which must move in lockstep with the
`Aspire.Hosting.*` versions or the AppHost throws at startup rather than at restore.

**Frozen surfaces.** `web/src/frozenHooks.test.ts` reads the app's own source and fails on any
rename or silent addition of a `data-testid` (39 hooks). Several accessible names (`Sign in`,
`Sign out`, `CVs`, `Search`, `Open the agents assistant`, …) and the rail/dock push contracts are
asserted by the e2e suite. §9 of the design-system manual lists them and why.

**Tests as evidence, not decoration.** Contrast floors are asserted against token pairs
(`theme/tokens.contrast.test.ts`) and rendered composites (`theme/components.test.tsx`) rather than
eyeballed. Print behaviour is settled in a real browser at print media, because jsdom can only show
that a rule was *emitted*, not that it *won*. A test that mirrors a token is not a freeze — assert
literals where a value must not drift.

**Issue tracking is Linear**, not GitHub: workspace `experttojob`, team `ExpertToJob`, issue keys
`EXP-*`. Issue state is the progress file. Branch names come from Linear's own `gitBranchName` (they
carry the issue key, which is what auto-links the PR). Branch from `main` — **no stacked PRs**.

Everything before 2026-09-20 was tracked in a previous workspace under `P1T-*` keys, and the repo is
full of them: commit messages, code comments and manuals all cite `P1T-nnn`. Those references are
**history, not addresses** — the issues behind them are not reachable from the current workspace, so
read a `P1T-*` mention as a pointer to the commit or manual that explains it, and never assume a
lookup will resolve. New work cites `EXP-*`.

**The Ralph loop** (`ralph/PROMPT.md`, `ralph/ralph-once.sh`) is an unattended agent that takes one
Linear issue per iteration: label `ready-for-agent`, state `Todo`, `blockedBy` respected as
load-bearing. It builds TDD, runs the full suite, opens a PR, and never merges. Issues written for
it need acceptance criteria precise enough to build from without asking.

**CI is the last word on green.** A pushed PR is not a landed one: watch the run to a conclusion
(`gh pr checks <pr>`, `gh run watch <id> --exit-status`) before calling a ticket done, and read the
actual assertion when it is red — `gh run view --log-failed` truncates on long jobs, so pull the run
log archive instead. Then say whose red it is. Red that is already on `main` (`gh run list --branch
main`) is a finding that gets its own issue, never a reason to loosen the assertion that caught it.
A test that passes locally and fails on CI is evidence about the test: the environments differ in
culture, time zone and build configuration, and all three have bitten this repo.

## Other agent configs

An OpenAI Codex config exists at `~/.codex/config.toml`. To bring anything from it into Claude
Code, reply `/import` to see what is importable, then `/import --yes=<digest>` to apply it. (If
`/import` is unavailable here, run `claude import` from a terminal.)

<!-- rtk-instructions v2 -->
# RTK (Rust Token Killer) - Token-Optimized Commands

## Golden Rule

**Always prefix commands with `rtk`**. If RTK has a dedicated filter, it uses it. If not, it passes through unchanged. This means RTK is always safe to use.

**Important**: Even in command chains with `&&`, use `rtk`:
```bash
# ❌ Wrong
git add . && git commit -m "msg" && git push

# ✅ Correct
rtk git add . && rtk git commit -m "msg" && rtk git push
```

## RTK Commands by Workflow

### Build & Compile (80-90% savings)
```bash
rtk cargo build         # Cargo build output
rtk cargo check         # Cargo check output
rtk cargo clippy        # Clippy warnings grouped by file (80%)
rtk tsc                 # TypeScript errors grouped by file/code (83%)
rtk lint                # ESLint/Biome violations grouped (84%)
rtk prettier --check    # Files needing format only (70%)
rtk next build          # Next.js build with route metrics (87%)
```

### Test (60-99% savings)
```bash
rtk cargo test          # Cargo test failures only (90%)
rtk go test             # Go test failures only (90%)
rtk jest                # Jest failures only (99.5%)
rtk vitest              # Vitest failures only (99.5%)
rtk playwright test     # Playwright failures only (94%)
rtk pytest              # Python test failures only (90%)
rtk rake test           # Ruby test failures only (90%)
rtk rspec               # RSpec test failures only (60%)
rtk test <cmd>          # Generic test wrapper - failures only
```

### Git (59-80% savings)
```bash
rtk git status          # Compact status
rtk git log             # Compact log (works with all git flags)
rtk git diff            # Compact diff (80%)
rtk git show            # Compact show (80%)
rtk git add             # Ultra-compact confirmations (59%)
rtk git commit          # Ultra-compact confirmations (59%)
rtk git push            # Ultra-compact confirmations
rtk git pull            # Ultra-compact confirmations
rtk git branch          # Compact branch list
rtk git fetch           # Compact fetch
rtk git stash           # Compact stash
rtk git worktree        # Compact worktree
```

Note: Git passthrough works for ALL subcommands, even those not explicitly listed.

### GitHub (26-87% savings)
```bash
rtk gh pr view <num>    # Compact PR view (87%)
rtk gh pr checks        # Compact PR checks (79%)
rtk gh run list         # Compact workflow runs (82%)
rtk gh issue list       # Compact issue list (80%)
rtk gh api              # Compact API responses (26%)
```

### JavaScript/TypeScript Tooling (70-90% savings)
```bash
rtk pnpm list           # Compact dependency tree (70%)
rtk pnpm outdated       # Compact outdated packages (80%)
rtk pnpm install        # Compact install output (90%)
rtk npm run <script>    # Compact npm script output
rtk npx <cmd>           # Compact npx command output
rtk prisma              # Prisma without ASCII art (88%)
rtk uv run <cmd>        # Compact uv project command output
```

### Files & Search (60-75% savings)
```bash
rtk ls <path>           # Tree format, compact (65%)
rtk read <file>         # Code reading with filtering (60%)
rtk grep <pattern>      # Search grouped by file (75%). Format flags (-c, -l, -L, -o, -Z) run raw.
rtk find <pattern>      # Find grouped by directory (70%)
```

### Analysis & Debug (70-90% savings)
```bash
rtk err <cmd>           # Filter errors only from any command
rtk log <file>          # Deduplicated logs with counts
rtk json <file>         # JSON structure without values
rtk deps                # Dependency overview
rtk env                 # Environment variables compact
rtk summary <cmd>       # Smart summary of command output
rtk diff                # Ultra-compact diffs
```

### Infrastructure (85% savings)
```bash
rtk docker ps           # Compact container list
rtk docker images       # Compact image list
rtk docker logs <c>     # Deduplicated logs
rtk kubectl get         # Compact resource list
rtk kubectl logs        # Deduplicated pod logs
```

### Network (65-70% savings)
```bash
rtk curl <url>          # Compact HTTP responses (70%)
rtk wget <url>          # Compact download output (65%)
```

### Meta Commands
```bash
rtk gain                # View token savings statistics
rtk gain --history      # View command history with savings
rtk discover            # Analyze Claude Code sessions for missed RTK usage
rtk proxy <cmd>         # Run command without filtering (for debugging)
rtk init                # Add RTK instructions to CLAUDE.md
rtk init --global       # Add RTK to ~/.claude/CLAUDE.md
```

## Token Savings Overview

| Category | Commands | Typical Savings |
|----------|----------|-----------------|
| Tests | vitest, playwright, cargo test | 90-99% |
| Build | next, tsc, lint, prettier | 70-87% |
| Git | status, log, diff, add, commit | 59-80% |
| GitHub | gh pr, gh run, gh issue | 26-87% |
| Package Managers | pnpm, npm, npx | 70-90% |
| Files | ls, read, grep, find | 60-75% |
| Infrastructure | docker, kubectl | 85% |
| Network | curl, wget | 65-70% |

Overall average: **60-90% token reduction** on common development operations.
<!-- /rtk-instructions -->