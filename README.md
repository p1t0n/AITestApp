# CV Manager

A .NET 10 + React (Vite) service to manage available employees — skills, qualifications,
work experience, and time-based availability — and render their CVs. An MCP server exposes
every operation on every entity to external AI agents over the same Application layer, and a
suite of built-in AI agents (Roster Q&A, CV Tailoring, Match, **JD Shortlist**) consumes it —
including **semantic search over career narratives** (pgvector RAG).
See [SPEC.md](SPEC.md).

## Features

- **Employee management** — CRUD for employees + languages, availability (capacity step-function),
  skills (catalog-backed), qualifications, experiences, achievements; assembled CV view.
- **MCP server** — 38 tools over the same Application layer, OAuth 2.1 (Keycloak) per-tool scopes.
- **Semantic roster search (RAG)** — employee narratives embedded into pgvector by a self-healing
  reconcile worker; `roster_semantic_search` answers "who has done X" by meaning, with evidence.
- **JD Shortlist** — paste a job description, get coverage-ranked candidates with per-requirement
  evidence and one-click drill-in to a full Match assessment.
- **AI agent widget** — dockable in-app assistant with Roster Q&A / Tailor CV / Match / Shortlist /
  Usage tabs; per-user token caps enforced server-side.
- **Auth** — passkey (WebAuthn) sign-in for the app; OAuth 2.1 service accounts for agents.
- **Retrieval evals** — frozen golden set + live regression gate + threshold-sweep CLI; retrieval
  quality is measured, not guessed (see `manuals/retrieval-eval-baseline.md`).
- **Demo data** — committed 500-employee synthetic roster + seeder CLI for realistic demos.

## Stack

- **Backend:** ASP.NET Core Web API (.NET 10), layered Domain / Application / Infrastructure / Web
- **MCP server:** ModelContextProtocol (Streamable HTTP), thin adapters over the Application layer, OAuth 2.1 (Keycloak) with per-tool scopes
- **AI agents:** Microsoft Agent Framework over provider-agnostic `IChatClient` (GitHub Models by default); embeddings via `text-embedding-3-small`
- **Vector search:** PostgreSQL + pgvector (cosine), EF Core mapping via Pgvector.EntityFrameworkCore
- **Frontend:** React + Vite + TypeScript, MUI, TanStack Query (+ vitest component tests)
- **Database:** PostgreSQL via EF Core
- **Validation:** FluentValidation (enforced in the Application layer, so REST and MCP validate identically)
- **Tests:** xUnit (unit + Testcontainers integration incl. pgvector) + vitest (frontend)

## Layout

```
api/
  Domain/          entities + enums
  Application/     services, DTOs, validators, CV assembly, search contracts  ← reused by Web + MCP
  Infrastructure/  EF Core DbContext, migrations, seeders, embeddings, pgvector search
  Web/             controllers, Swagger, passkey auth, DI
  Mcp/             MCP server: tools (incl. semantic + shortlist search), bearer auth, reconcile worker
  Agents/          AI agents service: Roster Q&A, CV Tailoring, Match, Shortlist + usage caps
web/               React SPA (incl. the agent widget)
tools/
  GenerateDemoRoster/  demo dataset generator (one-off, LLM-assisted)
  SeedDemoRoster/      demo roster seeder CLI (--count / --wipe)
  RetrievalEval/       retrieval eval + threshold-sweep CLI (+ RetrievalEval.Core)
keycloak/          realm-export.json (OAuth realm: clients, scopes, audience mapper)
manuals/           tech docs: semantic search + shortlist, eval baseline, decision records
tests/
  Application.Tests/  Application unit tests
  Agents.Tests/       agent + endpoint tests (fake chat client / tool source)
  Mcp.Tests/          MCP integration tests (in-process client, Testcontainers pgvector, Keycloak e2e)
docker-compose.yml Postgres (pgvector image) + Keycloak
SPEC.md            full design + decisions
```

## Run it

### 1. Start Postgres

```bash
docker compose up -d
```

### 2. Start the API

```bash
cd api/Web
dotnet run
```

On first run in Development it applies EF migrations and seeds the skill catalog + sample
employees. API listens on `http://localhost:5069`; Swagger UI at `http://localhost:5069/swagger`.

### 3. Start the SPA

```bash
cd web
npm install
npm run dev
```

Opens on `http://localhost:5173` and proxies `/api/*` to the backend.

### 4. Start the MCP server (optional)

The MCP server is an **OAuth 2.1 Resource Server**. Keycloak (the Authorization Server)
runs in `docker compose up -d` and imports a `cv-manager` realm with a public PKCE client
(`cv-manager-mcp`), the `mcp:read` / `mcp:write` / `mcp:admin` scopes, and an audience mapper.

```bash
cd api/Mcp
dotnet run
```

Config (`Mcp:Authority` = Keycloak realm issuer, `Mcp:Resource` = this server's audience)
defaults to the compose Keycloak. An MCP-capable agent discovers the AS via
`/.well-known/oauth-protected-resource`, runs the **authorization-code + PKCE** flow against
Keycloak, and calls tools with `Authorization: Bearer <access-token>`. Tokens are validated
against Keycloak's JWKS (issuer, audience, signature, lifetime). The server shares the API's
database. Dynamic Client Registration is enabled on the realm for self-service onboarding.

The MCP server binds `http://localhost:5100` (its launch profile).

### 5. Start the Agents service (optional)

AI agents built on the **Microsoft Agent Framework** that *consume* the MCP server (they hold
a `mcp:read` token from the `agent-roster-qa` Keycloak service-account client, so the MCP server
shows them read tools only). Needs a free **GitHub Models** PAT:

```bash
export GITHUB_TOKEN=<your-github-models-pat>
cd api/Agents
dotnet run
```

Binds `http://localhost:5200`. Four agents, one endpoint each (all also available as tabs in the
in-app widget):

- `POST /agents/roster-qa {question}` — Q&A over the roster; uses `roster_semantic_search` for
  capability questions and cites evidence snippets.
- `POST /agents/cv-tailoring {employeeId, jobDescription}` — tailoring guidance for one CV.
- `POST /agents/match {employeeId, jobDescription}` — gap analysis + scored fit assessment.
- `POST /agents/shortlist {jobDescription, availableOn?, skillIds?, location?, minYears?, topK?}` —
  JD → coverage-ranked candidates with per-requirement evidence (structured JSON).
- `GET /agents/usage` — the caller's token usage vs their daily/weekly/monthly caps.

Example:

```bash
curl -s http://localhost:5200/agents/roster-qa \
  -H 'Content-Type: application/json' \
  -d '{"question":"Who has built real-time payments systems?"}'
```

Requires the MCP server (step 4) + Keycloak (step 1) running. Model/auth/MCP-URL are configurable
in `api/Agents/appsettings.json`; the chat backend is provider-agnostic (`IChatClient`) and swaps
to Azure OpenAI / OpenAI / Anthropic / Ollama in one line. Every agent call is metered against
per-user token caps (defaults 25k/150k/500k daily/weekly/monthly).

### 6. Seed the demo roster (optional)

500 synthetic employees across 10 industries, with narratives rich enough to make semantic search
worth demoing:

```bash
dotnet run --project tools/SeedDemoRoster            # seed all 500 (idempotent)
dotnet run --project tools/SeedDemoRoster -- --wipe  # remove exactly the demo rows
```

With the MCP service running, the reconcile worker embeds the new employees automatically.
See `manuals/semantic-roster-search.md` for the full RAG documentation.

## MCP tools

38 tools, 1:1 thin adapters over the Application layer, annotated read-only / write /
destructive so clients can gate dangerous calls:

- **Employees:** `employee_list`, `employee_get`, `employee_create`, `employee_update`, `employee_delete`
- **Children:** `language_*`, `availability_*`, `employee_skill_*`, `qualification_*`, `experience_*`,
  `achievement_*`, `experience_skill_*`
- **Skill catalog:** `category_list/tree/create/update/delete`, `skill_list/create/update/delete`
- **CV:** `cv_get` (assembled data, not a PDF)
- **Semantic search (RAG):** `roster_semantic_search` (query by meaning + hard filters, evidence
  snippets), `roster_shortlist_search` (multi-requirement coverage-ranked candidate retrieval)

Each tool requires a scope: read-only tools need `mcp:read`, create/update need `mcp:write`,
deletes need `mcp:admin`. The server hides tools the token isn't scoped for and forbids the call.
Failures return a structured error (`not_found` / `conflict` / `validation` with per-field detail)
so an agent can self-correct.

## Tests

```bash
dotnet test          # backend: unit + Testcontainers integration (needs Docker)
cd web && npm test   # frontend: vitest component tests
```

Live tests (real embeddings / models) are opt-in: `dotnet test --filter "Category=live"` with
`GITHUB_TOKEN` set. The retrieval regression gate lives there too.

## Database migrations

```bash
dotnet ef migrations add <Name> \
  --project api/Infrastructure/EmployeeManager.Infrastructure.csproj \
  --startup-project api/Web/EmployeeManager.Web.csproj \
  --output-dir Persistence/Migrations
```

## API surface

- `GET/POST/PUT/DELETE /api/employees` — employee CRUD
- `GET /api/employees/{id}` — full detail
- `GET /api/employees/{id}/cv` — assembled CV
- `.../availability`, `.../skills`, `.../languages`, `.../qualifications`, `.../experiences` — child resources
- `GET/POST/DELETE /api/catalog/categories`, `/api/catalog/categories/tree`, `/api/catalog/skills` — skill catalog

## Not yet built (next increments)

- Server-side PDF rendering (CV is React-rendered; print to PDF for now)
- Web integration tests (WebApplicationFactory + Testcontainers) and Playwright e2e
- SPA edit forms for languages / qualifications / experiences (API already supports them)
- Shortlist-specific retrieval evals (requirement-extraction fidelity, coverage-merge ranking)
- CV-tailoring retrieval (reuse strong phrasings across CVs) and multi-turn agent memory
