# CV Manager

A .NET 10 + React (Vite) service to manage available employees — skills, qualifications,
work experience, and time-based availability — and render their CVs. Base project, designed
to be extended later with AI features (starting with an MCP server). See [SPEC.md](SPEC.md).

## Stack

- **Backend:** ASP.NET Core Web API (.NET 10), layered Domain / Application / Infrastructure / Web
- **Frontend:** React + Vite + TypeScript, MUI, TanStack Query
- **Database:** PostgreSQL via EF Core
- **Validation:** FluentValidation
- **Tests:** xUnit (Application unit tests)

## Layout

```
api/
  Domain/          entities + enums
  Application/     services, DTOs, validators, CV assembly   ← future MCP server reuses this
  Infrastructure/  EF Core DbContext, migrations, seed
  Web/             controllers, Swagger, DI
web/               React SPA
tests/             Application unit tests
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
- MCP server (Application layer is ready to be referenced)
- Integration tests (WebApplicationFactory + Testcontainers) and Playwright e2e
- SPA edit forms for languages / qualifications / experiences (API already supports them)
- Authentication
