# Semantic Roster Search & JD Shortlist (RAG)

Retrieval-augmented "find people by meaning" over employee CV narratives.

The structured MCP tools match on rows — skill tags, categories, availability. They can't answer
*"who has shipped real-time trading systems?"* or *"anyone with fintech + team-lead experience?"*,
because that meaning lives in free-text **experience summaries and achievements**, not tags. This
surface embeds those narratives, stores the vectors in pgvector, and exposes them two ways:

- **`roster_semantic_search`** — single-question retrieval; the Roster Q&A agent uses it to answer
  capability questions with cited evidence.
- **`roster_shortlist_search`** + **`ShortlistAgent`** — JD-driven candidate shortlisting: paste a
  job description, get coverage-ranked candidates with per-requirement evidence and a "Run full
  Match" drill-in.

Supporting machinery: a **retrieval eval harness** (frozen golden set, measured baseline, live
regression gate) and a **500-employee demo roster** (generator + seeder tooling).

> Core search: P1T-32…39 (design record: [`rag-semantic-roster-search-plan.md`](rag-semantic-roster-search-plan.md)).
> Shortlist + evals + demo data: P1T-40…56 (decision records on the wayfinder map P1T-40; measured
> baseline + verdicts: [`retrieval-eval-baseline.md`](retrieval-eval-baseline.md)).

---

## Architecture

```
┌─────────────┐  POST /agents/roster-qa      ┌──────────────────────────────┐
│  web (SPA)  │  POST /agents/shortlist      │ Agents svc (:5200)           │
│  Shortlist  │ ───────────────────────────► │  RosterQaAgent (all read     │
│  tab + Q&A  │                              │   tools incl. semantic srch) │
└─────────────┘                              │  ShortlistAgent (narrowed to │
                                             │   roster_shortlist_search)   │
                                             └──────────────┬───────────────┘
                                                            │ MCP over HTTP (bearer, mcp:read)
                                                            ▼
                                             ┌──────────────────────────────┐
                                             │ Mcp svc (:5100)               │
                                             │  RosterSearchTools            │
                                             │  RosterShortlistTools         │
                                             │    → ISemanticSearchService   │
                                             │    → IShortlistSearchService  │
                                             │  ReconcileWorker (hosted)     │
                                             └──────────────┬───────────────┘
                                                            │
                    ┌───────────────────────────────────────┼──────────────────────────────┐
                    ▼                                        ▼                              ▼
        ┌────────────────────────┐        ┌───────────────────────────┐     ┌────────────────────────┐
        │ Application             │        │ Infrastructure             │     │ GitHub Models           │
        │  ChunkProjection        │        │  EmployeeSearchChunk        │     │  text-embedding-3-small │
        │  Reconciler (pure diff) │        │  (pgvector table)           │     │  (OpenAI-compatible)    │
        │  ISemanticSearchService │        │  GitHubModelsEmbedder       │     └────────────────────────┘
        │  IShortlistSearchService│        │  SearchIndexReconciler      │
        │  ShortlistRanker (pure) │        │  SemanticSearchService      │
        │  IEmbedder (contract)   │        │  DemoRosterSeeder           │
        └────────────────────────┘        └───────────────────────────┘
                                                Postgres + pgvector

  tools/: GenerateDemoRoster · SeedDemoRoster · RetrievalEval(+Core, eval fixtures + sweep CLI)
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

## JD shortlist — `roster_shortlist_search` + `ShortlistAgent`

JD-driven candidate retrieval on the same substrate. A job description is too long and multi-faceted
to embed as one query (it averages into mush), so the flow splits it:

1. **`ShortlistAgent`** (name `shortlist`, tools narrowed to `roster_shortlist_search`) runs one
   two-turn session: turn 1 — the model distills the JD into 3–8 short requirement phrases and calls
   the tool once (user-set filters pass through verbatim); turn 2 — the model returns only minimal
   `[{"employeeId","rationale"}]` JSON.
2. **`roster_shortlist_search(requirements[], filters?, topK?)`** (MCP, `mcp:read`) batch-embeds all
   requirements in **one** embedding call, runs one cosine query per requirement over the pre-filtered
   chunk set, and merges **in code** (`ShortlistRanker`, pure): a candidate matches a requirement iff
   their best chunk similarity ≥ `MinSimilarity`; ranking is **coverage-first** (requirements-matched
   count, then mean best similarity). Per-requirement evidence (matched/missed + best snippet) rides
   along. Defaults: top 10, cap 20.
3. **`POST /agents/shortlist { jobDescription, availableOn?, skillIds?, location?, minYears?, topK? }`**
   composes the response **endpoint-side**: ids/names/scores/coverage/evidence come from the captured
   tool result (a per-run `DelegatingAIFunction` records it), the model contributes only rationales.
   Unknown ids are ignored; an unparseable rationale turn degrades to templated rationales
   ("Matched N/M requirements: …") — still 200. Upstream faults (model, tool soft error, tool never
   called) → 502.

**Response shape:**

```json
{
  "requirements": ["built real-time payments systems", "led a team"],
  "candidates": [{
    "employeeId": "…", "name": "Ada Lovelace", "title": "Payments Lead",
    "score": 0.82,
    "coverage": { "matched": 4, "total": 5 },
    "requirements": [{ "text": "…", "matched": true, "snippet": "…" }],
    "rationale": "…"
  }]
}
```

**Why the LLM never touches the numbers:** ranking math is deterministic tool code; the corruption
guard is tested (model returns wrong ids → response ids stay the tool's). Hard filters come from the
**UI**, never extracted from the JD — a hallucinated hard filter silently excludes good candidates.

**Widget**: the Shortlist tab takes the JD + optional filters (date, catalog-bound skill picker,
location, min years, topK), renders the requirement chips ("how the JD was read"), ranked candidate
cards (employee link, score, coverage badge, rationale, expandable evidence), and a **Run full
Match** action that jumps to the Match tab pre-filled with that employee + the same JD. Cap-reached
(429) and errors render like the other tabs; the Usage tab picks up the `shortlist` agent
automatically.

**Cost**: one shortlist ≈ 2 model turns (~3–6k tokens) against the caller's cap. Default caps were
raised to 25k / 150k / 500k (daily/weekly/monthly) — the old 1000/7000/30000 were demo placeholders.

---

## Retrieval evals

Retrieval quality is **measured, not vibed** (`tools/RetrievalEval.Core` + `tools/RetrievalEval`):

- **Frozen corpus** (24 hand-authored employees; keyword terms exclusive per employee) + **golden
  set** (39 labelled queries: keyword / paraphrase / cross-facet / negative). Fixtures live with the
  eval core; labels are versioned truth — never mix demo data in.
- **Metrics**: recall@5, MRR, negative-query false-positive rate (the trio the threshold trades
  between), plus keyword-subset recall@5 (feeds the hybrid question).
- **Live regression gate**: `dotnet test --filter "Category=live"` with `GITHUB_TOKEN` — real
  embeddings against Testcontainers pgvector, asserts no regression vs the committed floor.
- **Sweep CLI**: embeds once, re-ranks per threshold (a full sweep costs one run's embedding budget):
  `GITHUB_TOKEN=<pat> dotnet run --project tools/RetrievalEval -- --sweep 0.15:0.50:0.05 --refine`.

**Measured baseline + standing verdicts** (see [`retrieval-eval-baseline.md`](retrieval-eval-baseline.md)):
at 0.30 — recall@5 **1.0**, MRR **0.985**, negative-FP **0.0**, keyword recall **1.0**. Verdicts:
**`MinSimilarity` stays 0.30** (mid-plateau 0.285–0.350); **hybrid keyword+vector search not
adopted** (keyword gap 0.0 pts vs the >10-pt adoption rule) — the eval gate re-raises it if the gap
ever opens. Caveat: the small frozen corpus saturates recall by design; the gate guards regressions,
it doesn't claim perfection at scale.

---

## Demo data

500 synthetic employees across 10 industry clusters with rich career narratives
(`api/Infrastructure/Persistence/SeedData/demo-roster.json`, committed; all emails
`@demo.example.com`):

- **Generate/regenerate**: `tools/GenerateDemoRoster` — deterministic assembly from hand-authored
  career templates (seeded PRNG), optional LLM enrichment pass (`GITHUB_TOKEN`). The committed file
  is the deterministic output (seed 48).
- **Seed**: `dotnet run --project tools/SeedDemoRoster -- [--count N] [--wipe]` — idempotent by
  email; `--wipe` deletes exactly the `@demo.example.com` employees (cascades children + chunks).
  Or set `Seed:DemoRoster=true` (+ `Seed:DemoRosterCount`) for seed-on-boot demo environments.
- After seeding, the reconcile worker embeds the new chunks on its own (real embeddings; 500
  employees ≈ 75–150k embedding tokens once — infra cost, not user caps).

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
                   "MaxSnippetsPerEmployee": 3, "SnippetMaxChars": 500,
                   "ShortlistDefaultTopK": 10, "ShortlistMaxTopK": 20 }
```

Agents service `appsettings.json` (shortlist + caps):

```jsonc
"McpAuth": { "shortlist": { "ClientId": "agent-shortlist", "Scope": "mcp:read", … } },
"Usage":   { "DefaultDailyTokens": 25000, "DefaultWeeklyTokens": 150000, "DefaultMonthlyTokens": 500000 }
```

Web service `appsettings.json` (demo seeding, off by default):

```jsonc
"Seed": { "DemoRoster": false, "DemoRosterCount": null }
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
- **MCP transport** (`Mcp.Tests`, in-memory host, stubbed service): both search tools exposed under
  `mcp:read`, ranked results returned, params (incl. `requirements[]`) bound through correctly.
- **Shortlist** (`Application.Tests` + `Mcp.Tests` + `Agents.Tests`): pure `ShortlistRanker` ranking
  (4-of-5 beats 1-of-5-at-0.9), pgvector coverage-first end-to-end, one-batched-embed-call assertion,
  composer corruption guard (model can't change ids), degrade-to-templated-rationale, endpoint
  contract/429/502, live smoke.
- **Agent** (`Agents.Tests`, fake chat client + fake tools): capability question routes to
  `roster_semantic_search` and cites the snippet; a soft error falls back to `employee_list`.
- **Evals** (`Mcp.Tests` + `tools/RetrievalEval.Core`): pure metric math, fixture-integrity checks,
  Testcontainers plumbing test with a deterministic embedder, live-gated regression test with real
  embeddings.
- **Frontend** (`web`, vitest + testing-library — the project's first FE harness): 10 Shortlist-tab
  component tests (payload shaping, gating, rendering, evidence, drill-in, 429, empty state).
  `cd web && npm test`.

Docker-in-CI is required for the Testcontainers suites.

---

## Resolved decisions & remaining risks

Resolved (with measurements — details in [`retrieval-eval-baseline.md`](retrieval-eval-baseline.md)):

- **JD-shortlist**: shipped (tool + agent + endpoint + widget tab), see the shortlist section above.
- **Threshold**: measured; `MinSimilarity` stays 0.30 (mid-plateau 0.285–0.350 on the golden set).
- **Hybrid keyword+vector search**: **not adopted** — keyword-subset recall showed zero gap. The
  pre-decided design (tsvector + GIN, `websearch_to_tsquery`, RRF k=60, transparent in
  `SemanticSearchService`) stays on record in the P1T-46 resolution; every future sweep's
  keyword-recall column re-raises the question automatically.

Remaining:

- **Secret hygiene**: never commit a real PAT — use `GITHUB_TOKEN`. (A PAT was found committed in
  `api/Agents/appsettings.json` during this work; rotate it.)
- **Scale**: revisit an HNSW index if the roster grows into the tens of thousands of chunks; rerun
  the sweep if the corpus or embedding model changes (recall saturates on the small frozen corpus).
- **Shortlist-specific evals** (requirement-extraction fidelity, coverage-merge ranking against
  labelled JDs): post-build follow-on, out of the original map's scope.
- **CV-tailoring retrieval / multi-turn memory**: parked fog from the planning map (P1T-40), not
  started.
