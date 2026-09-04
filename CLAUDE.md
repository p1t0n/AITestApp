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
dotnet test --filter "Category=live"           # real model/embeddings; needs GEMINI_API_KEY
dotnet test --filter "Category=eval"           # tool-selection gate, ~3 min, 39 model calls
```

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

`docker compose up -d` brings up Postgres (pgvector image), Keycloak (imports the
`expert-to-job` realm), and the Aspire dashboard (`:18888`, OTLP on `:4317` — the app runs fine
when it is down). Then, each in its own terminal:

| Process | Command | Port |
|---|---|---|
| Web API | `dotnet run` in `api/Web` | 5069 (Swagger at `/swagger`) |
| SPA | `npm run dev` in `web` | 5173 |
| MCP server | `dotnet run` in `api/Mcp` | 5100 |
| Agents | `GEMINI_API_KEY=… dotnet run` in `api/Agents` | 5200 |

The Web API applies migrations and seeds the catalog + sample experts on first Development run.
Agents needs the MCP server *and* Keycloak up. `dotnet run --project tools/SeedDemoRoster` adds
500 synthetic experts (`--wipe` removes exactly those rows).

## Architecture

Five processes: Web API · MCP server · Agents · SPA · (Postgres + Keycloak).

**The Application layer is the single behaviour seam.** `api/Application` holds services, DTOs,
FluentValidation validators and CV assembly. Both `api/Web` (REST controllers) and `api/Mcp` (tool
adapters) are thin shells over it, which is why REST and MCP validate identically. A rule added in
a controller instead of the Application layer silently does not apply to MCP.

**Agents never reference the Application layer or the database.** `api/Agents` reaches the roster
only through the MCP server over Streamable HTTP, holding a Keycloak client-credentials token whose
scope decides which tools it is even shown — capability is enforced by the token, not by the
prompt. Deterministic facts (an expert's data, counts, stats) come from captured MCP results and
are composed in code; the model writes prose. Orchestration composes the single-agent runs and
degrades a failed stage rather than failing the call. `IChatClient` keeps the provider swappable
(Gemini free tier by default).

**Two backends, one token.** The SPA talks to `/api/*` (5069) and `/agents/*` (5200) through two
axios instances (`web/src/api/http.ts`) that attach the same bearer. The Web host issues the
session JWT and both hosts validate it with a shared signing key. Vite proxies both in dev;
`VITE_API_TARGET` / `VITE_AGENTS_TARGET` retarget them for e2e.

**The SPA has no React Context.** Session and theme mode are hand-rolled `useSyncExternalStore`
subscriptions (`web/src/auth/session.ts`, `web/src/theme/mode.ts`). Server state is TanStack Query.

**Styling goes through the theme, not the component.** MUI 5 with a token layer in
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

**Frozen surfaces.** `web/src/frozenHooks.test.ts` reads the app's own source and fails on any
rename or silent addition of a `data-testid` (39 hooks). Several accessible names (`Sign in`,
`Sign out`, `CVs`, `Search`, `Open the agents assistant`, …) and the rail/dock push contracts are
asserted by the e2e suite. §9 of the design-system manual lists them and why.

**Tests as evidence, not decoration.** Contrast floors are asserted against token pairs
(`theme/tokens.contrast.test.ts`) and rendered composites (`theme/components.test.tsx`) rather than
eyeballed. Print behaviour is settled in a real browser at print media, because jsdom can only show
that a rule was *emitted*, not that it *won*. A test that mirrors a token is not a freeze — assert
literals where a value must not drift.

**Issue tracking is Linear**, not GitHub: team `P1t0ns nest`, project `AI Test Manager`. Issue
state is the progress file. Branch names come from Linear's own `gitBranchName` (they carry the
issue key, which is what auto-links the PR). Branch from `main` — **no stacked PRs**.

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