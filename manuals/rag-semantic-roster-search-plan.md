# Semantic Roster Search (RAG) — Implementation Plan

Retrieval-augmented "find people by meaning" over expert CV narratives.
Today the roster is only keyword/structured-queryable (skill rows, categories,
availability). This adds semantic retrieval over the free-text career narratives
so agents can answer *"who has shipped real-time trading systems?"* or
*"anyone with fintech + team-lead experience?"* — meaning that lives in prose,
not skill tags.

---

## Locked decisions

| # | Decision | Choice |
|---|----------|--------|
| 1 | Feature | Semantic Roster Search over expert narratives |
| 2 | Retrieval boundary | **New MCP tool** — respects "all expert data via MCP" rule |
| 3 | Vector store | **pgvector** in existing Postgres (no new infra) |
| 4 | Embedding unit | **Per-`Experience` chunk** (title@company+dates+summary+achievements) **+ one `Expert.Summary` chunk** |
| 5 | Embedding model | `text-embedding-3-small` (1536 dims), cosine, **no ANN index v1** (flat scan) |
| 6 | Indexing | **Background worker**, reconciliation-by-hash; same path serves backfill |
| 7 | Tool output | **Ranked experts + evidence snippets + score** |
| 8 | Filtering | **Vector + SQL pre-filter** (availability / skillIds / location / minYears) |
| 9 | Consumer (v1) | **RosterQaAgent only**; JD-shortlist on MatchAgent = follow-on |
| 10 | Usage/caps | **Untouched**; embedding tokens logged separately as infra cost |
| 11 | Storage shape | **Dedicated `ExpertSearchChunk` projection table** |
| 12 | Dirty-tracking | **Reconciliation-by-hash** (self-healing, catches every write path) |
| 13 | Ops | `pgvector/pgvector` image; `CREATE EXTENSION` in migration; no ANN index |
| 14 | Robustness | Min cosine **0.30** (config), **topK 5** / max 20, empty-on-none, structured error → graceful degrade |
| 15 | Tests | Fake `IEmbeddingGenerator` for logic; Testcontainers `pgvector` for ranking SQL |

---

## Architecture

```
┌─────────────┐   POST /agents/roster-qa    ┌──────────────────────────────┐
│  web (SPA)  │ ──────────────────────────► │ Agents svc (:5200)           │
└─────────────┘                             │  RosterQaAgent                │
                                            │  - loads all mcp:read tools   │
                                            │  - now incl. roster_semantic_ │
                                            │    search; instruction tweak  │
                                            └──────────────┬───────────────┘
                                                           │ MCP over HTTP (bearer, mcp:read)
                                                           ▼
                                            ┌──────────────────────────────┐
                                            │ Mcp svc (:5100)               │
                                            │  RosterSearchTools            │
                                            │    → ISemanticSearchService   │
                                            │  ReconcileWorker (BackgroundS)│
                                            └──────────────┬───────────────┘
                                                           │
                        ┌──────────────────────────────────┼───────────────────────────┐
                        ▼                                   ▼                           ▼
             ┌────────────────────┐          ┌──────────────────────┐      ┌───────────────────────┐
             │ Application         │          │ Infrastructure        │      │ GitHub Models          │
             │  SemanticSearchSvc  │          │  ExpertSearchChunk   │      │  text-embedding-3-small│
             │  ChunkProjection    │          │  (pgvector table)      │      │  (OpenAI-compatible)   │
             │  (render + hash)    │          │  IEmbeddingGenerator   │      └───────────────────────┘
             └────────────────────┘          └──────────────────────┘
                                                 Postgres + pgvector
```

**Query flow (the "RAG" path):**
1. RosterQa gets a natural-language question, decides to call `roster_semantic_search(query, filters?, topK?)`.
2. Tool → `ISemanticSearchService`: embed the query string (`IEmbeddingGenerator`).
3. SQL: pre-filter `ExpertSearchChunk` (join Expert for availability/skill/location/minYears), order by `embedding <=> $q` cosine, drop rows below threshold 0.30, take chunks, aggregate to experts by `MAX(similarity)`, keep top-5 with their best 1–3 snippets.
4. Tool returns `{ results: [{ expertId, name, title, score, snippets[] }], error? }`.
5. RosterQa generates a **cited** answer from the snippets; drills into `cv_get` only when it needs full detail. On empty/error, falls back to structured tools.

**Indexing flow (steady state = backfill):**
- `ReconcileWorker` (BackgroundService in Mcp svc) every N seconds:
  1. Build **desired** chunk set from domain via `ChunkProjection` (render text + `ContentHash`).
  2. Diff against existing `ExpertSearchChunk` by hash → upsert changed (`Embedding=null`), delete orphans.
  3. Embed every row where `Embedding IS NULL` in batches; log embedding token counts.
- First run finds everything unembedded → bootstraps the whole roster. No separate backfill code path.

---

## Data model

New entity `ExpertSearchChunk` (Domain/Entities, mapped in `AppDbContext`, precedent = `AgentUsage`):

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `ExpertId` | `Guid` | FK → Expert, for aggregation + pre-filter join; cascade delete |
| `SourceType` | enum | `Experience` \| `Summary` |
| `SourceId` | `Guid` | `Experience.Id`, or `Expert.Id` for the summary chunk |
| `Content` | `text` | the exact rendered text that was embedded |
| `ContentHash` | `text` | SHA-256 of `Content`; drives dirty detection |
| `Embedding` | `vector(1536)` null | via `Pgvector.EntityFrameworkCore`; null = needs embedding |
| `Model` | `text` | embedding model id used |
| `EmbeddedAt` | `timestamptz` null | when embedding was written |

- Unique index `(SourceType, SourceId)` — one chunk per source.
- Dirty predicate: `Embedding IS NULL OR ContentHash <> <recomputed>` (recompute lives in reconcile diff, not SQL).
- New dependency: `Pgvector.EntityFrameworkCore` (Infrastructure).

**Chunk rendering (`ChunkProjection`):**
- Experience chunk = `"{Title} @ {Company} ({StartDate}–{EndDate|present})\n{Summary}\n{achievement texts joined}"`.
- Summary chunk = `Expert.Summary` (skip if null/blank).
- Deterministic string → deterministic hash → deterministic tests.

---

## Work breakdown (vertical slices, dependency order)

Small tracer-bullet slices; each is independently reviewable and leaves the app green.

### Slice 1 — pgvector foundation
- docker-compose: stock `postgres` → `pgvector/pgvector:pg<major>` (confirm major first).
- EF migration: `CREATE EXTENSION IF NOT EXISTS vector;` + `ExpertSearchChunk` table with `vector(1536)` column, `(SourceType,SourceId)` unique index, FK+cascade.
- `Pgvector.EntityFrameworkCore` package; `ExpertSearchChunk` entity + `AppDbContext` mapping (`HasPostgresExtension("vector")`, `.HasColumnType("vector(1536)")`).
- **Accept:** migration applies on the pgvector image; empty table exists; existing suites green.

### Slice 2 — embedding client + token logging
- `GitHubModels.EmbeddingModel = "text-embedding-3-small"` config key.
- Register `IEmbeddingGenerator<string, Embedding<float>>` in Infrastructure DI, same endpoint+PAT as chat (via `Microsoft.Extensions.AI.OpenAI`).
- Thin `IEmbeddingLog` (ILogger or `AgentUsage`-style row, synthetic name `embedding-index`) — infra cost visibility only, **caps untouched**.
- **Accept:** a smoke test embeds a string → 1536-float vector; token count logged.

### Slice 3 — chunk projection + reconciliation core (pure, unit-tested)
- `ChunkProjection`: domain → desired `List<DesiredChunk{SourceType,SourceId,ExpertId,Content,Hash}>`.
- `Reconciler.Diff(desired, existing)` → `{ upserts, deletes }` (pure function).
- Unit tests with **fake `IEmbeddingGenerator`**: add/edit/delete/reorder achievement, null summary, orphan removal.
- **Accept:** diff logic fully covered, no DB/network.

### Slice 4 — reconciliation worker
- `ReconcileWorker : BackgroundService` in Mcp svc: interval loop → `ChunkProjection` → `Reconciler.Diff` → upsert (`Embedding=null`) / delete → embed nulls in batches → log tokens.
- Interval + batch size in config.
- **Accept:** integration test (Testcontainers pgvector + fake embedder) — seed roster, run once, chunks materialized with vectors; edit → re-run → only changed chunk re-embedded; delete expert → chunks gone.

### Slice 5 — semantic search query service
- `ISemanticSearchService.SearchAsync(query, filters, topK)` (Application) → embed query, pre-filter SQL (availableOn/skillIds/location/minYears), cosine order, threshold 0.30, aggregate chunks→experts `MAX(sim)`, top-5, attach best 1–3 snippets.
- Structured error result on embed failure (no throw).
- **Accept:** Testcontainers pgvector test — known vectors rank in expected order; pre-filter excludes non-matches; sub-threshold → empty; embed failure → `{error, results:[]}`.

### Slice 6 — MCP tool
- `RosterSearchTools.SemanticSearch` — `[McpServerTool(Name="roster_semantic_search", ReadOnly=true)]`, `[Authorize(McpScopes.Read)]`, DI `ISemanticSearchService`. Params: `query`, optional `availableOn/skillIds/location/minYears`, `topK`.
- **Accept:** `Mcp.Tests` — tool exposed under `mcp:read`, rejects without scope, returns ranked experts+snippets on a seeded fixture.

### Slice 7 — RosterQa consumes it
- Instruction tweak: prefer `roster_semantic_search` for capability/experience/"who has done X" questions; structured tools for hard facts; on error/empty fall back to structured tools and say semantic search was unavailable.
- No endpoint/UI change (tool auto-loaded via all-read-tools path).
- **Accept:** Agents test with faked MCP/tool — a "who has done X" question triggers the tool; error path degrades gracefully.

### Follow-on (separate issue, not this slice)
- **JD-shortlist mode on MatchAgent** (decision #9 "B"): embed a JD, retrieve top candidates, score each. Changes Match's contract (id+JD → JD-only) + endpoint + UI tab. Log as next P1T issue once retrieval is proven.

---

## Testing summary
- **Fake `IEmbeddingGenerator`** (deterministic hash→vector) for slices 3–7 logic: fast, no network.
- **Testcontainers `pgvector/pgvector`** for slices 4–5: real cosine ranking + pre-filter SQL + `CREATE EXTENSION`/migration. (Docker-in-CI confirmed available.)
- **`Mcp.Tests`** auth/exposure parity for the new tool.
- Reconciler core kept a pure method; `BackgroundService` a thin scheduler around it.

## Risks / notes
- **⚠️ Security (do first, out of band):** `api/Agents/appsettings.json` commits a real GitHub PAT. Rotate the token and move it to a secret/env var before touching this feature.
- **Embedding availability:** confirm the GitHub Models PAT actually serves an embedding deployment; if not, point the embedding client at OpenAI-direct/Azure via config (chat stays on GitHub Models).
- **Threshold 0.30** is a starting guess for `text-embedding-3-small` cosine — validate against real roster data during Slice 4/5 and adjust the config default.
- **Scale:** flat scan is fine at hundreds–low-thousands of chunks. Add HNSW only if query latency shows up; the `vector` column is already index-ready.
