# CV Manager

A .NET 10 + React (Vite) service to manage available employees — skills, qualifications,
work experience, and time-based availability — and render their CVs. An MCP server exposes
every operation on every entity to external AI agents over the same Application layer.
See [SPEC.md](SPEC.md).

## Stack

- **Backend:** ASP.NET Core Web API (.NET 10), layered Domain / Application / Infrastructure / Web
- **MCP server:** ModelContextProtocol (Streamable HTTP), thin adapters over the Application layer
- **Frontend:** React + Vite + TypeScript, MUI, TanStack Query
- **Database:** PostgreSQL via EF Core
- **Validation:** FluentValidation (enforced in the Application layer, so REST and MCP validate identically)
- **Tests:** xUnit (Application unit tests + MCP integration tests)

## Layout

```
api/
  Domain/          entities + enums
  Application/     services, DTOs, validators, CV assembly   ← reused by both Web and MCP
  Infrastructure/  EF Core DbContext, migrations, seed
  Web/             controllers, Swagger, DI
  Mcp/             MCP server: tools, bearer auth, error mapping
web/               React SPA
tests/
  Application.Tests/  Application unit tests
  Mcp.Tests/          MCP integration tests (in-process client)
docker-compose.yml Postgres
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

```bash
cd api/Mcp
dotnet run
```

Serves MCP over Streamable HTTP. A bearer token is **required** — set it first
(the server denies all requests when no key is configured):

```bash
dotnet user-secrets set "Mcp:ApiKey" "<your-token>"   # or env: Mcp__ApiKey
```

Point any MCP-capable agent at the server URL with header
`Authorization: Bearer <your-token>`. It shares the API's database/connection string.

## MCP tools

36 tools, 1:1 thin adapters over the Application layer, annotated read-only / write /
destructive so clients can gate dangerous calls:

- **Employees:** `employee_list`, `employee_get`, `employee_create`, `employee_update`, `employee_delete`
- **Children:** `language_*`, `availability_*`, `employee_skill_*`, `qualification_*`, `experience_*`,
  `achievement_*`, `experience_skill_*`
- **Skill catalog:** `category_list/tree/create/update/delete`, `skill_list/create/update/delete`
- **CV:** `cv_get` (assembled data, not a PDF)

Failures return a structured error (`not_found` / `conflict` / `validation` with per-field detail)
so an agent can self-correct.

## Tests

```bash
dotnet test
```

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
- MCP auth hardening: OAuth 2.1 / the MCP authorization spec (static bearer key for now)
- Web integration tests (WebApplicationFactory + Testcontainers) and Playwright e2e
- SPA edit forms for languages / qualifications / experiences (API already supports them)
- Authentication for the Web API
