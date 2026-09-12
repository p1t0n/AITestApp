# ADR: one command replaces compose and four terminals

**Status:** accepted, 2026-09-12. Supersedes the "Running the stack" table in `README.md` and
`CLAUDE.md`, and retires `docker-compose.yml`. Charted as Linear map P1T-201, whose nine decision
tickets carry the evidence behind every claim here.

## The decision

An Aspire **AppHost** owns the whole local stack — Postgres (pgvector), Keycloak, the Web API, the
MCP server, the Agents host and the Vite SPA — plus a one-shot migrator and an on-demand demo-roster
seeder. `docker-compose.yml` is deleted. A shared `ServiceDefaults` project gives the .NET hosts one
telemetry and health-check spine. `dotnet run --project api/AppHost` is the only documented way to
bring the stack up.

Aspire 13.5.3, on the .NET 10 SDK. No workload, no `aspire` CLI required, no prerelease packages.

## What was weighed

Four rungs were on the table, each including the last:

1. **AppHost orchestration only** — start the five processes and the two containers.
2. **+ `ServiceDefaults`** — shared OTLP export, health checks, resilience.
3. **+ hosting integrations** — Postgres and Keycloak declared in C#, compose retired.
4. **+ a deployment manifest** — azd, Container Apps, a real cloud target.

**Rung 3 is what this ADR adopts.** Rung 4 is out of scope and stays out: it is a cloud-spend
decision, not a refactor, and it drags Azure cost and identity choices behind it that have nothing
to do with the daily loop. The alternative of staying where we were — compose plus a standalone
dashboard container, which is what `main` carried before this — was rejected for one reason: the
pain being solved is *four terminals and an env var*, and rungs 1 and 2 leave two of the terminals
standing.

## Vocabulary

Defined here rather than in `CONTEXT.md`, which is a domain glossary and stays one. None of these
are ExpertToJob concepts; they are Aspire's.

- **AppHost** — the orchestrator project (`api/AppHost`). Declares resources in C# and starts them.
- **Resource** — anything the AppHost starts: a container, a .NET project, a Vite app, a one-shot
  executable.
- **`ServiceDefaults`** — a library (`api/ServiceDefaults`) every .NET host references for the
  OTLP exporter, generic instrumentation, health checks and HTTP resilience.
- **Proxy port** — the port a client connects to. Aspire listens there and forwards to the resource,
  which binds a random port of its own. `docker ps` shows the random one; it is not the contract.
- **Explicit start** — a resource declared but not started with the stack, launched on demand from
  the dashboard.

## What is decided, and why

**Compose is deleted rather than kept as a fallback.** Two ways to start a stack means two configs
drifting, and the only consumer that genuinely needs raw containers is the e2e runner, which already
makes its own with `docker run`.

**Every endpoint is pinned. There is no service discovery.** This reverses the "hybrid" intent the
map started with, and the reason is concrete: nothing in this codebase can consume a
`https+http://servicename` URL. The MCP client is a hand-built `new HttpClient`
(`api/Agents/Mcp/McpToolSource.cs`), the Keycloak token provider concatenates strings off
`Authority`, and the Vite proxy is a Node process. Adopting discovery would mean refactoring the MCP
transport — a change to auth-critical code, with its own tests, which is its own ticket if anyone
ever wants it.

So: Web 5069, Mcp 5100 and Agents 5200 come from their own launch profiles at no AppHost cost; the
SPA is pinned to 5173 with `WithEndpoint`; Postgres keeps 5432 and Keycloak 8080. **The consequence
is that the checked-in literals stay true** — `web/vite.config.ts`, every `appsettings.json`, the
realm export and the documented port table need no edit at all.

**The SPA's 5173 is pinned rather than injected**, specifically because CORS is not configurable:
`api/Web/Program.cs` carries `.WithOrigins("http://localhost:5173", …)` as literals in code, and
`AddPasskeyAuth` reads `Auth:Passkey:Origins` eagerly at startup. A moving SPA origin would mean
editing the CORS policy and the auth surface to serve an orchestration convenience, on the one
subsystem with no graceful failure mode.

**Migrations leave `api/Web` for a one-shot `api/Migrator`.** `api/Mcp` and `api/Agents` both call
`AddInfrastructure`, so Web did not merely seed on their behalf — it owned the schema for two
services that never reference it, and `api/Mcp` against a fresh database has never worked alone.
The migrator applies `MigrateAsync` and `DbInitializer.SeedAsync`; all three hosts
`WaitForCompletion(migrator, exitCode: 0)`. `ServiceManagerBootstrapper` stays in Web: it runs in
every environment including production, and moving it would pull a production path into a
dev-orchestration change.

**`ServiceDefaults` is adopted by composition, not replacement.** The generated template wires
AspNetCore, HttpClient and Runtime instrumentation and nothing else — none of the six sources
`api/Agents` subscribes, none of the four in `api/Mcp`. Dropping it in as-is would silently stop
collecting every gen_ai span, every MAF workflow span and every MCP RPC while still looking like it
worked. So the shared project owns the exporter and generic instrumentation; each host keeps its own
`AddSource`/`AddMeter` list next to the code that emits them, and its own
`ConfigureResource(r => r.AddService("experttojob-…"))`. AppHost resource names match those service
names, so telemetry and the resource list never disagree.

**`AddStandardResilienceHandler()` stays; `AddServiceDiscovery()` goes.** The fear that a resilience
handler would silently retry model calls is structurally impossible today — the Gemini and MCP
clients are both hand-built, and the handler only attaches to `IHttpClientFactory` clients. The one
factory client in the stack is the Keycloak token provider, where retrying is desirable. Discovery,
by contrast, has no consumer at all, and a file advertising a capability the stack deliberately
refused is how the next person comes to believe `https+http://mcp` resolves.

**No prerelease NuGet package enters `ExpertToJob.slnx`.** A standing rule, not a per-package
argument, because the fork recurs with every Aspire integration outside the core. It is why Keycloak
is a plain `AddContainer` rather than `AddKeycloak`: that package has never shipped stable (0 stable
releases across 46 versions), and everything it was wanted for — realm import via
`WithContainerFiles`, a `/health/ready` check via `WithHttpHealthCheck`, OTLP via
`WithOtlpExporter` — is reachable from the stable package in about six lines.

**The AppHost is SDK-only: `AspireUseCliBundle=false`.** Under bundle mode a resolution failure is
*error* ASPIRE009, and with the AppHost in the solution that takes down the entire
`dotnet build`/`dotnet test` step over a tool nobody installed. CI needs nothing added. The `aspire`
CLI is not a prerequisite; it is installed ad hoc when Aspire itself is upgraded, since `aspire
update` is the only supported upgrade path.

## What does not change

`dotnet test` and `npm run test:e2e` keep their current shape and stay green. Every Testcontainers
fixture calls `MigrateAsync` against its own container and never sees the AppHost. The e2e runner
keeps its own ports (55433/5079/5174/5175) and its own database — it gains exactly one step, running
the migrator between container-up and API-start, because the schema no longer arrives with the Web
host.

A single `dotnet run` in one project still works. Each project keeps getting its port from its own
launch profile, with or without the AppHost.

## Tripwires

Things that will silently do the wrong thing, each learned the hard way:

- **`WithImage` must precede `WithDataVolume`.** The mount path is chosen by parsing the image tag
  *at call time*, and Aspire's default Postgres is now 18, which moved `PGDATA`. Wrong order mounts
  the 18 path and initdb writes a fresh empty cluster into the same volume, with no error, while the
  seeded data sits invisible.
- **The volume is `aitestapp_experttojob-pgdata`.** Compose prefixes the project name; the compose
  key `experttojob-pgdata` names no Docker object, and passing it creates a new empty volume.
- **Keycloak gets no data volume, ever.** Keycloak skips realm import when the realm already exists
  and has no startup override, so a volume freezes the realm at its first import.
- **Keycloak health is on port 9000 and needs `KC_HEALTH_ENABLED=true`.** It never appears on 8080.
  And `/health/ready` returns an empty check list: it means the HTTP layer is up, not that the realm
  exists.
- **`WithReference(db)` defaults its config key to the resource name.** This app reads
  `ConnectionStrings:Default`, so the reference must pass `connectionName: "Default"` — otherwise it
  falls back to its hardcoded `localhost:5432` literal and *appears* to work.
- **There is no `NuGet.config`, and that is load-bearing.** The Aspire SDK resolves from nuget.org
  before any `PackageReference` is considered, so adding a restricted-feed `NuGet.config` breaks the
  AppHost build in a way that looks nothing like a package problem.
- **The resilience handler becomes a real concern** the moment someone moves the Gemini client onto
  `IHttpClientFactory`. Retried model calls cost money and the free tier has a daily cap.

## Consequences accepted

- A solo `dotnet run --project api/Web` against a fresh database no longer self-migrates. That is
  the point — no host is secretly responsible for another's schema — and the README says to run the
  migrator or start the AppHost.
- `WaitForCompletion` is incompatible with `dotnet watch`
  ([microsoft/aspire#9756](https://github.com/microsoft/aspire/issues/9756), open). Nothing here uses
  watch, and the dashboard can restart a resource.
- OTLP export becomes conditional on `OTEL_EXPORTER_OTLP_ENDPOINT` being set, where today both hosts
  export unconditionally and drop when nothing listens. Both satisfy the boots-without-OTLP
  invariant; the new behaviour is quieter, and the invariant gets its own test rather than resting
  on the template's guard.

## Evidence

Every claim above was measured, not assumed. A skeleton AppHost (Postgres, Keycloak, Web) was built
and run before this was written: the seeded volume survived two boots with its rows intact, the
realm imported from a copied file with `Mounts: []`, and `ConnectionStrings__Default` and
`OTEL_EXPORTER_OTLP_ENDPOINT` were read off the running process. The spike also contradicted its own
brief in three places — pinned ports turned out to be proxy ports, the Web process runs on a dynamic
`ASPNETCORE_URLS`, and a realm edit takes effect on the next start rather than live.

The four research notes and the spike branch are referenced from the Linear tickets under P1T-201.
Note that `docs/` is gitignored, so those notes are local to whoever ran them; the tickets carry the
findings that matter.
