# ADR: main runs on the .NET 11 release candidate

**Status:** accepted, 2026-10-01. Built by EXP-73, which retargeted all 24 projects from `net10.0`
to `net11.0`. The question that gated it — whether `Pgvector.EntityFrameworkCore` survives EF Core
11 — was answered by [EXP-72](https://linear.app/experttojob/issue/EXP-72) before a line of this
moved. Keep this document short; it records a choice, not the diff.

## The decision

`main` targets **`net11.0`** and compiles on the **.NET 11 SDK release candidate**,
`11.0.100-rc.1.26425.128`, pinned in `global.json` with `allowPrerelease: true` and
`rollForward: latestPatch`. CI installs the same band (`dotnet-version: '11.0.x'`,
`dotnet-quality: 'preview'`) in both jobs that build.

RC1 ships under a **go-live licence**, so this is a supported configuration rather than a preview
gamble — and the reason to take it is that this repo exists to learn the stack. Sitting a release
behind teaches nothing. The GA bump is already written down as
[EXP-78](https://linear.app/experttojob/issue/EXP-78), which cannot start before 2026-11-10.

`TargetFrameworkLockstepTests` (in `tests/ServiceDefaults.Tests`) holds the three files that have to
agree — every csproj, `global.json`, and `.github/workflows/ci.yml` — at `dotnet test`. They do not
fail at restore when they drift; they fail as one project quietly left behind, or as a CI runner
resolving a different SDK from the one the repo is pinned to.

## Why 11 and not 10, when 10 is the LTS

The support dates make the LTS/STS distinction nearly worthless here:

| Release | Track | Support ends |
|---|---|---|
| .NET 11 | STS | 2028-11-09 |
| .NET 10 | LTS | 2028-11-14 |

**Five days apart.** The usual reason to prefer an LTS — a longer runway before a forced migration —
does not exist between these two. Choosing 10 would buy a working week of support in exchange for
being a release behind for three years.

## The gate, and what it actually proved

EF Core 11 is `net11.0`-only, so the retarget had to bring EF 11 and
`Npgsql.EntityFrameworkCore.PostgreSQL` 11 with it. `Pgvector.EntityFrameworkCore` is still at
**0.3.0**, built for `net8.0` against `Npgsql.EntityFrameworkCore.PostgreSQL >= 9.0.1`, with no EF 11
release and no upstream statement about one. Semantic search, exemplar search, the shortlist and the
index reconciler all sit on it.

EXP-72 answered that on a spike branch — draft PR #226, CI run 36831341753, all three jobs green —
with every Pgvector-backed suite passing and `dotnet ef migrations has-pending-model-changes`
reporting no model diff. EF 11 + Pgvector 0.3.0 reads `vector(1536)` exactly as EF 10 did.

**That result is narrower than it looks, and the narrowness is the point.** Pgvector 0.3.0 passing
does not mean upstream supports EF 11; it means nothing this repo exercises touches what changed
between EF 10 and EF 11. This ADR inherits that exposure rather than clearing it. A future EF 11
servicing release could move it, and the first symptom would be a red Pgvector suite, not a restore
error. No upstream issue was filed, because there is nothing to report.

## Alternatives

**Stay on .NET 10.** Rejected on the support-date table above. It also parks the repo on EF Core 10
indefinitely, since EF 11 cannot be referenced from `net10.0` at all.

**Wait for .NET 11 GA (2026-11-10).** Rejected as a *default*, not as a possibility — the wait is
about five weeks, the licence already permits production, and EXP-72 had already measured the one
risk worth measuring. The counter-argument is real: an RC can still take a breaking change before
GA, and when it does, this repo eats it. That cost was accepted as the price of the first paragraph.

**Multi-target `net10.0;net11.0`.** Rejected. It doubles every build and every test run to hedge a
migration nobody intends to reverse, and EF 11's `net11.0`-only packages mean the `net10.0` leg
could not reference the same EF anyway.

## The exit

.NET 12 is the next LTS, **November 2027**. That is the planned move, and the retarget rehearsed for
it: the version surface is three files and one test, not 24 hand-edited projects. Before then,
EXP-78 takes the RC to GA.

## What the move actually forced

Almost nothing, which is the useful finding:

* **One package reference dropped.** SDK 11's package pruning reports
  `Microsoft.Extensions.DependencyInjection.Abstractions` in `api/Application` as redundant
  (`NU1510`); it is removed. This is an SDK 11 change, not an EF 11 one.
* **One workaround retired.** The direct `Microsoft.EntityFrameworkCore.Relational` reference
  existed only to settle an MSB3277 version conflict between EF 10 and Npgsql 10. On EF 11 +
  Npgsql 11 they agree, so the reference and its `PackageVersion` are gone and nothing in
  `Directory.Packages.props` pins a transitive version any more.
* **No application or test code changed.** None of the EF 11 or ASP.NET Core 11 breaking changes
  that were checked applied. The Migrator has migrations, so EF 11's new "`Migrate()` throws with
  no migrations" does not reach it, and `tests/Migrator.Tests` says so. The five hosted services —
  four `BackgroundService` subclasses plus `EmbeddingsProviderPeriodWriter` — each already handle
  their own exceptions, so nothing was added and `BackgroundServiceExceptionBehavior` is still
  unset. `HostTelemetryFreezeTests` and `ServiceDefaultsBootTests` pass **unmodified** against
  Hosting's new default HTTP semantic-convention tags.
* **No out-of-band package was forced.** Microsoft.Extensions.AI, MCP, Agent Framework, Pgvector,
  Testcontainers, OpenTelemetry and Swashbuckle all stayed. In particular
  `Microsoft.AspNetCore.OpenApi` 11 did **not** clash with Swashbuckle 10.2.3, so it stays too —
  though it remains dead code, since `AddOpenApi`/`MapOpenApi` are never called. Replacing the
  Swashbuckle generator with the built-in one is
  [EXP-74](https://linear.app/experttojob/issue/EXP-74).

C# 15 adoption is deliberately not here. `LangVersion` is pinned nowhere — the language follows the
target framework — and the features are taken one issue at a time (EXP-75, EXP-76, EXP-77).
