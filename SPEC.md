# ExpertToJob — SPEC (base / POC)

A .NET + React service to manage a roster of available employees — their skills,
qualifications, work experience, and time-based availability — and render their CVs
from that data. Built as a base project and now extended with an **MCP server** that
exposes every operation on every entity to external AI agents over the same Application
layer, protected by OAuth 2.1.

## Goals

- Store and fully manage employee professional data.
- Render a CV for any employee from the stored data.
- Let external AI agents perform all operations via MCP, reusing the data logic with zero duplication.
- Be a clean training/POC foundation, not a finished product.

## Out of scope (deferred to later exercises)

- Server-side PDF generation (CV is React-rendered for now).
- AI-tailored / curated CVs.
- Authentication for the **Web API** (the MCP server is OAuth-protected; the REST API is not yet).

---

## Tech stack

| Layer       | Choice                                                        |
|-------------|---------------------------------------------------------------|
| Front end   | React + Vite + TypeScript, MUI, TanStack Query                |
| Back end    | ASP.NET Core Web API (**.NET 10**), Controllers + DTOs + Swagger |
| MCP server  | ModelContextProtocol (Streamable HTTP), thin adapters over Application |
| Hosting     | Separate API + MCP server + standalone Vite dev server with proxy |
| Database    | PostgreSQL via EF Core (JSONB available)                      |
| Validation  | FluentValidation (enforced **in the Application layer** — REST + MCP validate identically) |
| Local infra | Docker Compose for Postgres + Keycloak                        |
| Auth        | MCP server: OAuth 2.1 Resource Server (Keycloak AS, PKCE), per-tool scopes. Web API: none yet |

The MCP server runs as a third sibling alongside the API and SPA, referencing the
Application layer directly.

---

## Solution layout

```
/api
  Domain/          entities, enums
  Application/     services, use-cases, DTOs, FluentValidation, CV-assembly  ← Web + MCP reuse this
  Infrastructure/  EF Core, DbContext, migrations, seed
  Web/             controllers, Swagger, DI wiring
  Mcp/             MCP server: tools, OAuth (JWT) auth, scope policies, error mapping
/web               React SPA (Vite)
/keycloak          realm-export.json (OAuth realm: clients, scopes, audience mapper,
                   client-registration policy, OAuth 2.1 client profile)
/tests
  Application.Tests  Application unit tests (xUnit + FluentValidation)
  Mcp.Tests          MCP integration tests (in-process client) + Keycloak e2e
docker-compose.yml Postgres + Keycloak
```

**Layering rule:** both the Web API and the MCP server reference **Application**.
No business logic lives in controllers or MCP tools — they are thin adapters.

---

## Domain model

### Employee (aggregate root)
- `FirstName`, `LastName`, `Title`, `Email`, `Phone`, `Location`
- `Summary` / bio
- `PhotoUrl`
- Children:
  - **SpokenLanguage** — `Language`, `Level`
  - **AvailabilityEntry** — see capacity below
  - **EmployeeSkill** — see skills below
  - **Qualification** — see below
  - **Experience** — see below

### Availability (time-based capacity)
Availability is a **step function over time**, not a single flag.

- **AvailabilityEntry** — `EffectiveFrom` (date), `CapacityPercent` (int, 0–100)
- `CapacityPercent` means **percent available** (e.g. "available at 50%").
- Capacity at any target date = the entry with the greatest `EffectiveFrom ≤ date`;
  it holds until the next entry overrides it.
- Step-function model → no overlapping ranges possible.

Example: `50%` from 2027-04-01, `75%` from 2027-07-01, `100%` from November.

### Skills
- **Category** — self-referencing tree (`ParentId`), e.g. `Languages > JavaScript > React`.
- **Skill** — `Name`, belongs to a `Category`.
- **EmployeeSkill** (junction) — `Level` enum (Beginner → Intermediate → Advanced → Expert),
  `YearsExperience`.

### Qualification (single entity)
One table with a `Type` enum (`Degree` | `Certification`) and nullable fields per type:
- Common: `Type`, `Name`
- Degree: `Institution`, `Field`, `StartDate`, `EndDate`
- Certification: `Issuer`, `CredentialId`, `IssueDate`, `ExpiryDate`

**Accepted trade-off:** merging into one entity yields sparse/nullable columns
(vs two clean tables). Chosen for fewer tables.

### Experience
- `Company`, `Title`, `Location`, `StartDate`, `EndDate` (nullable = current), `Summary`
- Children (each with its own Application service + MCP tools):
  - **Achievement** — ordered bullet points (`Order`, `Text`)
  - **ExperienceSkill** — links to `Skill` used in that role (evidence trail for AI:
    "used React at Acme 2020–22")

---

## Features

- **Full CRUD** (UI + API) for: employees and all children, the skill catalog
  (categories + skills), qualifications, experience, achievements, availability entries.
- **MCP server**: 36 tools (1:1 over the Application services) exposing every operation on
  every entity to external AI agents. Read/write/destructive annotations; structured tool
  errors (`not_found` / `conflict` / `validation` with per-field detail) for agent
  self-correction; `cv_get` assembles a CV.
- **OAuth 2.1**: the MCP server is a Resource Server (validates JWTs, serves
  `/.well-known/oauth-protected-resource`); Keycloak is the Authorization Server (PKCE,
  Dynamic Client Registration). Per-tool scopes — `mcp:read` / `mcp:write` / `mcp:admin`.
  Registration is initial-access-token gated (anonymous is refused) and capped at `mcp:read`
  plus the audience mapper, so a client cannot register its way to write capability; the
  OAuth 2.1 baseline is stamped onto it at registration rather than remembered per client
  (`manuals/mcp-dcr-policy.md`).
- **Seed data**: a seeded skill catalog + 3–5 sample employees with complete data.
- **CV rendering**: React-only live view; renders **all** sections in a fixed, sensible
  order (full dump). Export via browser print → PDF for now.

---

## Testing

- **Application unit tests** (xUnit) — capacity-at-date step function, CV assembly, skill
  mapping, FluentValidation rules, and the relocated service-boundary validation; plus the
  new `AchievementService` / `ExperienceSkillService`.
- **MCP integration tests** — an in-process MCP client over `WebApplicationFactory` (InMemory
  DB, locally-minted JWTs): tool registry, CRUD round-trips, structured errors, OAuth gate,
  scope filtering, audience binding.
- **Keycloak e2e** (Testcontainers, `Category=e2e`) — a real Keycloak issues a token; the
  server validates it via JWKS. Run `dotnet test --filter "Category!=e2e"` to skip it.
- **Later:** Web API integration tests (WebApplicationFactory + Testcontainers Postgres) and
  Playwright end-to-end tests. Structure already supports slotting these in.

---

## Accepted trade-offs (revisit later)

1. **Qualification merge** → nullable/sparse columns instead of two clean tables.
2. **React-only CV** → CV export logic lives in the browser; the MCP/AI server cannot
   render a PDF headlessly until a server-side render path is added.

---

## Agent layer (Microsoft Agent Framework)

A fourth sibling process — `/api/Agents` — hosts AI agents built on the **Microsoft Agent
Framework (MAF)**. Agents do **not** reference the Application layer; they call the existing
**MCP server** over HTTP via MAF's native MCP client, reusing the 36 tools with zero
duplication. This is a training/POC layer: the bar is idiomatic MAF + tests, not product polish.

### Topology

Five processes total: Web API · MCP server · **Agents** · SPA · (Postgres + Keycloak).
The Agents service exposes REST endpoints; a SPA chat/action UI is deferred.

### Integration & auth

- **Tool access:** MAF MCP client → MCP server over Streamable HTTP. No tool re-declaration.
- **Identity:** each agent is a Keycloak **client-credentials** service account carrying the
  **minimal scope** it needs. The MCP server's scope-filtering means a `mcp:read`-only agent
  is never even shown write/destructive tools — capability is enforced by the token, not by prompt.
- **Model:** code is provider-agnostic via `IChatClient` (Microsoft.Extensions.AI). Default
  wiring is **GitHub Models** (free, PAT-authenticated, OpenAI-compatible, strong tool-calling);
  swappable to Azure OpenAI / OpenAI / Anthropic / Ollama in one line.

### Structure

- An `IAgent` abstraction with one class per agent under `/api/Agents`.
- Shared DI services: MCP-client factory, client-credentials token acquisition, thread store.
- New agents slot in as a class + an endpoint; infra (Keycloak client, compose service) is shared.

### Agents

**Built now — Roster Q&A** (`mcp:read`):
- A `ChatClientAgent` over the MCP read tools, conversational.
- **In-memory threaded sessions** — a per-user thread store replays the last 10 turns of
  question/answer text (bounded, no summarizer); the REST contract takes/returns a `threadId`
  so follow-ups keep context. 30-min sliding TTL, 20 threads/user, lost on restart — fine for POC.
- Returns a natural-language answer + `threadId`; employees are cited by name **and id** so the
  SPA can deep-link later. Read-only scope ⇒ structurally cannot mutate data.

**Recorded for future (not built):**
- **CV Tailoring** (`mcp:read`) — target role/JD → select relevant skills/experience →
  tailored CV. Pattern: structured output + prompt design. (Realises the "AI-tailored CVs" step.)
- **Resume Ingestion** (`mcp:write`) — raw resume/LinkedIn text → populate Employee + children.
  Pattern: structured extraction → chained tool-call writes + validation-error self-correction.
- **Staffing / Match** (`mcp:read`) — a need (skills + level + time window) → rank available
  employees by skill match × capacity-at-date. Pattern: multi-agent MAF orchestration
  (skill-matcher + availability-checker + ranker sub-agents).

### Testing

- **Deterministic unit tests** with a fake `IChatClient` — assert tool wiring, thread state,
  MCP plumbing, error mapping. No live model in CI.
- **Live smoke tests** behind `Category=live` (skipped by default, mirrors the `Category=e2e`
  Keycloak pattern): hit the real model + real MCP loop on demand.

---

## Likely next steps (after this base)

- Server-side PDF render path (e.g. QuestPDF) reusable headlessly.
- AI-tailored CVs: select relevant skills/experience for a target role.
- Web API integration + Playwright e2e test layers.
- Auth for the Web API (JWT or ASP.NET Identity).
- MCP auth hardening: external IdP. (The tighter DCR policy shipped — see above.)
