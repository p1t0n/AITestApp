# Semantic Roster Search (RAG)

Retrieval-augmented "find people by meaning" over employee CV narratives.

The structured MCP tools match on rows — skill tags, categories, availability. They can't answer
*"who has shipped real-time trading systems?"* or *"anyone with fintech + team-lead experience?"*,
because that meaning lives in free-text **experience summaries and achievements**, not tags. This
feature embeds those narratives, stores the vectors in pgvector, and exposes an MCP tool
(`roster_semantic_search`) that the Roster Q&A agent uses to retrieve relevant employees and answer
with cited evidence.

> Implemented across P1T-32 … P1T-39. Design/decision record: [`manuals/rag-semantic-roster-search-plan.md`](rag-semantic-roster-search-plan.md).

---

## Architecture

```
┌─────────────┐   POST /agents/roster-qa    ┌──────────────────────────────┐
│  web (SPA)  │ ──────────────────────────► │ Agents svc (:5200)           │
└─────────────┘                             │  RosterQaAgent                │
                                            │  loads all mcp:read tools,    │
                                            │  incl. roster_semantic_search │
                                            └──────────────┬───────────────┘
                                                           │ MCP over HTTP (bearer, mcp:read)
                                                           ▼
                                            ┌──────────────────────────────┐
                                            │ Mcp svc (:5100)               │
                                            │  RosterSearchTools            │
                                            │    → ISemanticSearchService   │
                                            │  ReconcileWorker (hosted)     │
                                            └──────────────┬───────────────┘
                                                           │
                    ┌──────────────────────────────────────┼──────────────────────────────┐
                    ▼                                       ▼                              ▼
        ┌────────────────────────┐        ┌──────────────────────────┐     ┌────────────────────────┐
        │ Application             │        │ Infrastructure            │     │ GitHub Models           │
        │  ChunkProjection        │        │  EmployeeSearchChunk       │     │  text-embedding-3-small │
        │  Reconciler (pure diff) │        │  (pgvector table)          │     │  (OpenAI-compatible)    │
        │  ISemanticSearchService │        │  GitHubModelsEmbedder      │     └────────────────────────┘
        │  IEmbedder (contract)   │        │  SearchIndexReconciler     │
        └────────────────────────┘        │  SemanticSearchService     │
                                           └──────────────────────────┘
                                                Postgres + pgvector
```

**Boundary rule.** All employee-data access goes through MCP tools, never the DB — semantic search
over narratives *is* employee-data access, so it ships as an MCP tool. Any MCP client gets it under
`mcp:read`; the existing Keycloak scope model covers it with no new auth.

**Layering.** The retrieval *contracts* and the *pure* logic (projection, diff) live in
Application, free of EF and pgvector types. The pgvector/embedding *implementations* live in
Infrastructure. The background *scheduler* lives in the Mcp service.

---

## Data model

`EmployeeSearchChunk` (in `Infrastructure/Persistence`, not Domain — it carries the Postgres-specific
`Vector` type and is a derived read-model, not domain state):

| Column | Type | Notes |
|--------|------|-------|
| `Id` | uuid | PK |
| `EmployeeId` | uuid | FK → Employee, cascade delete; scopes pre-filter + aggregation |
| `SourceType` | text (enum) | `Experience` \| `Summary` |
| `SourceId` | uuid | Experience id, or Employee id for a Summary chunk |
| `Content` | text | the exact rendered text that was embedded |
| `ContentHash` | text (64) | SHA-256 hex of `Content`; the dirty signal |
| `Embedding` | `vector(1536)` null | null until embedded |
| `Model` | text | embedding model id used |
| `EmbeddedAt` | timestamptz null | when the embedding was written |

Unique index `(SourceType, SourceId)` — one chunk per source. `EmployeeId` index for filtering.
Mapped only under Npgsql; ignored for the in-memory test provider (which can't map `Vector`).

### Chunk granularity & rendering (`ChunkProjection`)

- **One chunk per `Experience`**: header `"{Title} @ {Company} ({yyyy-MM}–{yyyy-MM|present})"`, then
  the experience `Summary`, then achievement texts (ordered by `Order`) as `- ` bullets.
- **One chunk for the employee `Summary`** (skipped when blank).

Rendering is pure and deterministic, so identical content always yields the same `ContentHash` —
that's what makes change detection exact.

---

## Indexing (backfill = steady-state)

`SearchIndexReconciler.RunOnceAsync` (Infrastructure), driven by `ReconcileWorker` (a hosted
`BackgroundService` in the Mcp service) every `IntervalSeconds`:

1. **Sync** — project every employee → desired chunks; diff against persisted chunks by
   `(SourceType, SourceId)` (`Reconciler.Diff`). Content-hash change ⇒ update in place +
   clear the embedding; new source ⇒ insert (embedding null); vanished source ⇒ delete.
2. **Embed** — every chunk with a null embedding is embedded in batches of `EmbedBatchSize`;
   the vector, model id, and timestamp are written.

A fresh or edited chunk has a null embedding, so the same loop **backfills a cold index and keeps a
warm one current** — there is no separate backfill path. A failed pass is logged and retried on the
next tick (dirty chunks stay dirty), so the index self-heals across every write path (Web API, MCP
write tools, even raw SQL). Employee deletes cascade the chunks away.

**Embedding cost** is logged per batch (`embedding-index: … N input tokens`) for visibility. It is
**not** charged against per-user token caps — embeddings are an infra/operational cost, distinct
from the chat generation the caps meter.

---

## Query flow — `roster_semantic_search`

MCP tool (`mcp:read`, read-only) → `ISemanticSearchService.SearchAsync`:

1. Embed the query string.
2. Apply optional **hard filters** as a SQL pre-filter, so the top-K are all valid candidates:
   - `availableOn` — capacity > 0 on the date (availability step-function: latest entry on/before the date).
   - `skillIds` — has *all* of these catalog skills (each meeting `minYears` if set).
   - `location` — case-insensitive match.
   - `minYears` — applied to the required skills, or to any skill if no `skillIds` given.
3. Rank chunks by cosine similarity (`embedding <=> query`); drop anything below `MinSimilarity`
   (0.30) so an off-topic query returns nothing rather than the least-bad rows.
4. Aggregate chunk hits → employees by **best** similarity, take top-K (default 5, max 20), attach
   up to 3 truncated evidence snippets each.

**Tool parameters:** `query` (required); optional `availableOn`, `skillIds`, `location`,
`minYears`, `topK`.

**Result shape:**

```json
{
  "results": [
    { "employeeId": "…", "name": "Ada Lovelace", "title": "Payments Lead",
      "score": 0.88, "snippets": ["Payments Lead @ BankCo (2019-03–present)\nLed the payments rewrite."] }
  ],
  "error": null
}
```

**Graceful degrade.** If the embedding backend fails, the service returns
`{ "results": [], "error": "…" }` — it never throws. The Roster Q&A agent is instructed to fall back
to the structured tools and say semantic search was unavailable.

### Agent behavior

`RosterQaAgent` already loads every `mcp:read` tool, so it picks up `roster_semantic_search`
automatically. Its instructions steer capability/experience questions ("who has done X") to semantic
search and to quote the returned snippets as evidence; exact facts (skill levels, availability dates,
languages) still use the structured tools. `MatchAgent` and `CvTailoringAgent` are unchanged — they
stay narrowed to `cv_get`.

---

## Operations

- **Postgres image**: `pgvector/pgvector:pg17` (stock `postgres:17` plus the `vector` extension).
- **Extension + table**: created by the `AddEmployeeSearchChunk` EF migration (the `vector` extension
  is emitted from `HasPostgresExtension("vector")`).
- **No ANN index in v1**: at hundreds–low-thousands of chunks a flat cosine scan over the pre-filtered
  set is fine, and it sidesteps HNSW's under-return on selective filters. The `vector` column is
  index-ready — add HNSW only if query latency shows up.

### Configuration

Mcp service `appsettings.json`:

```jsonc
"GitHubModels":  { "Endpoint": "…", "EmbeddingModel": "text-embedding-3-small", "ApiKey": "" },
"SearchIndex":   { "Enabled": true, "IntervalSeconds": 30, "EmbedBatchSize": 32 },
"SemanticSearch":{ "MinSimilarity": 0.30, "DefaultTopK": 5, "MaxTopK": 20,
                   "MaxSnippetsPerEmployee": 3, "SnippetMaxChars": 500 }
```

The embedding PAT is read from the `GITHUB_TOKEN` env var (preferred) or `GitHubModels:ApiKey`.
The worker is disabled in the in-memory MCP tests via `SearchIndex:Enabled=false`.

---

## Testing

- **Pure/unit** (`Application.Tests`): `ChunkProjection` (rendering, blank-summary skip, ordered
  achievements, hash stability, reorder-changes-hash), `Reconciler.Diff`
  (insert/no-op/update/delete/mixed), and `GitHubModelsEmbedder` (batch shape, token count, logging)
  with a deterministic fake generator. Plus a skippable live embedding smoke test
  (`Category=live`, needs `GITHUB_TOKEN`).
- **Integration** (`Mcp.Tests`, Testcontainers `pgvector/pgvector:pg17`, fake embedder):
  `SearchIndexReconciler` (backfill, no-op second pass, edit re-embeds only the changed chunk,
  orphan delete, employee cascade) and `SemanticSearchService` (topical ranking + threshold
  exclusion, off-topic empty, location + skill pre-filters, embed-failure soft error).
- **MCP transport** (`Mcp.Tests`, in-memory host, stubbed service): tool exposed under `mcp:read`,
  ranked employees + snippets returned, query/filters/topK bound through correctly.
- **Agent** (`Agents.Tests`, fake chat client + fake tools): capability question routes to
  `roster_semantic_search` and cites the snippet; a soft error falls back to `employee_list`.

Docker-in-CI is required for the Testcontainers suites.

---

## Follow-ons & risks

- **JD-shortlist mode on `MatchAgent`** (deferred, separate issue): embed a job description, retrieve
  top candidates, score each. Changes Match's contract (id+JD → JD-only), endpoint, and UI tab.
- **Threshold tuning**: `MinSimilarity = 0.30` is a starting value for `text-embedding-3-small`
  cosine. Validate against real roster data and adjust the config key.
- **Embedding availability**: confirm the GitHub Models PAT actually serves an embedding deployment;
  if not, point `GitHubModels:Endpoint`/`EmbeddingModel` at OpenAI-direct/Azure (chat is unaffected).
- **Secret hygiene**: never commit a real PAT — use `GITHUB_TOKEN`. (A PAT was found committed in
  `api/Agents/appsettings.json` during this work; rotate it.)
- **Scale**: revisit an HNSW index if the roster grows into the tens of thousands of chunks.
