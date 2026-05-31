# Employee Manager + CV — SPEC (base / POC)

A .NET + React service to manage a roster of available employees — their skills,
qualifications, work experience, and time-based availability — and render their CVs
from that data. This is a **base project**: it is deliberately scoped to be extended
later with AI functionality, starting with an MCP server that manages all employee data.

## Goals

- Store and fully manage employee professional data.
- Render a CV for any employee from the stored data.
- Be structured so a future MCP server reuses the same data logic with zero duplication.
- Be a clean training/POC foundation, not a finished product.

## Out of scope (deferred to later exercises)

- Authentication / authorization.
- Server-side PDF generation (CV is React-rendered for now).
- AI-tailored / curated CVs.
- The MCP server itself (only the architectural seam is prepared now).

---

## Tech stack

| Layer       | Choice                                                        |
|-------------|---------------------------------------------------------------|
| Front end   | React + Vite + TypeScript, MUI, TanStack Query                |
| Back end    | ASP.NET Core Web API (**.NET 10**), Controllers + DTOs + Swagger |
| Hosting     | Separate API + standalone Vite dev server with proxy          |
| Database    | PostgreSQL via EF Core (JSONB available)                      |
| Validation  | FluentValidation (in Application layer)                       |
| Local infra | Docker Compose for Postgres                                   |
| Auth        | None (added later as its own exercise)                        |

The future MCP server joins as a third sibling alongside the API and SPA, referencing
the Application layer directly.

---

## Solution layout

```
/api
  Domain/          entities, enums
  Application/     services, use-cases, DTOs, FluentValidation, CV-assembly  ← MCP reuses this
  Infrastructure/  EF Core, DbContext, migrations, seed
  Web/             controllers, Swagger, DI wiring
/web               React SPA (Vite)
/tests             Application unit tests (xUnit + FluentValidation)
docker-compose.yml Postgres
```

**Layering rule:** both the Web API and the future MCP server reference **Application**.
No business logic lives in controllers or the eventual MCP tools — they are thin adapters.

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
- Children:
  - **Achievement** — ordered bullet points (`Order`, `Text`)
  - **ExperienceSkill** — links to `Skill` used in that role (evidence trail for AI:
    "used React at Acme 2020–22")

---

## Features (base build)

- **Full CRUD** (UI + API) for: employees and all children, the skill catalog
  (categories + skills), qualifications, experience, achievements, availability entries.
- **Seed data**: a seeded skill catalog + 3–5 sample employees with complete data.
- **CV rendering**: React-only live view; renders **all** sections in a fixed, sensible
  order (full dump). Export via browser print → PDF for now.

---

## Testing

- **Now:** Application-layer unit tests (xUnit) — capacity-at-date step function,
  CV assembly, skill mapping — plus FluentValidation rule tests.
- **Later:** API integration tests (WebApplicationFactory + Testcontainers Postgres)
  and Playwright end-to-end tests. Structure the solution so these slot in without rework.

---

## Accepted trade-offs (revisit later)

1. **Qualification merge** → nullable/sparse columns instead of two clean tables.
2. **React-only CV** → CV export logic lives in the browser; the MCP/AI server cannot
   render a PDF headlessly until a server-side render path is added.

---

## Likely next steps (after this base)

- Server-side PDF render path (e.g. QuestPDF) reusable headlessly.
- MCP server exposing employee-data management tools over the Application layer.
- AI-tailored CVs: select relevant skills/experience for a target role.
- Integration + Playwright e2e test layers.
- Auth (JWT or ASP.NET Identity).
