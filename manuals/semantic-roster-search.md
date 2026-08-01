# Semantic Roster Search & JD Shortlist (RAG)

Retrieval-augmented "find people by meaning" over employee CV narratives.

The structured MCP tools match on rows — skill tags, categories, availability. They can't answer
*"who has shipped real-time trading systems?"* or *"anyone with fintech + team-lead experience?"*,
because that meaning lives in free-text **experience summaries and achievements**, not tags. This
surface embeds those narratives, stores the vectors in pgvector, and exposes them three ways:

- **`roster_semantic_search`** — single-question retrieval; the Roster Q&A agent uses it to answer
  capability questions with cited evidence.
- **`roster_shortlist_search`** + **`ShortlistAgent`** — JD-driven candidate shortlisting: paste a
  job description, get coverage-ranked candidates with per-requirement evidence and a "Run full
  Match" drill-in.
- **`style_exemplar_search`** + **`CvTailoringAgent`** — CV bullet rewriting: tailoring returns
  before/after achievement-bullet rewrites shaped by anonymized strong-phrasing exemplars retrieved
  from *other* employees' CVs, vetted by a fabrication guard, applied via the user's own session.

Supporting machinery: a **retrieval eval harness** (frozen golden set, measured baseline, live
regression gate) and a **500-employee demo roster** (generator + seeder tooling).

The **staffing pipeline** (`POST /agents/staffing`) composes the shortlist and match steps
described here into one streamed, recommendation-first report — see
[`staffing-pipeline.md`](staffing-pipeline.md).

> Core search: P1T-32…39 (design record: [`rag-semantic-roster-search-plan.md`](rag-semantic-roster-search-plan.md)).
> Shortlist + evals + demo data: P1T-40…56 (decision records on the wayfinder map P1T-40; measured
> baseline + verdicts: [`retrieval-eval-baseline.md`](retrieval-eval-baseline.md)).
> Bullet rewriting: P1T-57…68 (wayfinder map P1T-57; gate prototype verdict P1T-58).

---

## Architecture

```
┌─────────────┐  POST /agents/roster-qa      ┌──────────────────────────────┐
│  web (SPA)  │  POST /agents/shortlist      │ Agents svc (:5200)           │
│  Shortlist +│  POST /agents/cv-tailoring   │  RosterQaAgent (all read     │
│  Tailor CV  │ ───────────────────────────► │   tools incl. semantic srch) │
│  tabs + Q&A │                              │  ShortlistAgent (narrowed to │
└─────────────┘                              │   roster_shortlist_search)   │
                                             │  CvTailoringAgent (cv_get +  │
                                             │   style_exemplar_search)     │
                                             └──────────────┬───────────────┘
                                                            │ MCP over HTTP (bearer, mcp:read)
                                                            ▼
                                             ┌──────────────────────────────┐
                                             │ Mcp svc (:5100)               │
                                             │  RosterSearchTools            │
                                             │  RosterShortlistTools         │
                                             │  RosterStyleTools             │
                                             │    → ISemanticSearchService   │
                                             │    → IShortlistSearchService  │
                                             │    → IExemplarSearchService   │
                                             │  ReconcileWorker (hosted)     │
                                             └──────────────┬───────────────┘
                                                            │
                    ┌───────────────────────────────────────┼──────────────────────────────┐
                    ▼                                        ▼                              ▼
        ┌────────────────────────┐        ┌───────────────────────────┐     ┌────────────────────────┐
        │ Application             │        │ Infrastructure             │     │ Gemini                  │
        │  ChunkProjection        │        │  EmployeeSearchChunk        │     │  gemini-embedding-001   │
        │  Reconciler (pure diff) │        │  (pgvector table)           │     │  (OpenAI-compatible)    │
        │  ISemanticSearchService │        │  GeminiEmbedder       │     └────────────────────────┘
        │  IShortlistSearchService│        │  SearchIndexReconciler      │
        │  IExemplarSearchService │        │  SemanticSearchService      │
        │  ShortlistRanker (pure) │        │  ExemplarSearchService      │
        │  ExemplarQualityFilter/ │        │  DemoRosterSeeder           │
        │   Anonymizer (pure)     │        └───────────────────────────┘
        │  IEmbedder (contract)   │             Postgres + pgvector
        └────────────────────────┘

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
| `SourceType` | text (enum) | `Experience` \| `Summary` \| `Achievement` |
| `SourceId` | uuid | Experience id, Employee id for a Summary chunk, or Achievement id for a bullet chunk |
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
- **One chunk per non-blank `Achievement` bullet** (trimmed text; `SourceId` = the achievement id):
  the fine-grained unit for style-exemplar retrieval (P1T-63). The parent experience chunk keeps the
  bullets rolled in as the employee-level narrative unit, so a bullet edit re-embeds exactly two
  chunks: the bullet's own and its parent experience chunk. Achievement chunks are **excluded** from
  the two employee-level search paths — see the bullet-rewriting section for the measured why.

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
   (0.55 for gemini-embedding-001) so an off-topic query returns nothing rather than the least-bad rows. Achievement bullet
   chunks are excluded from this pool (shared with the shortlist path — see bullet rewriting).
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
languages) still use the structured tools. `MatchAgent` stays narrowed to `cv_get`;
`CvTailoringAgent` is narrowed to `cv_get` + `style_exemplar_search` (see bullet rewriting below).

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
(The daily default was later raised again to 50k for the staffing pipeline, P1T-75.)

---

## CV bullet rewriting — `style_exemplar_search` + the 2-turn `CvTailoringAgent`

Tailoring output gains vetted **before/after rewrites of the CV's achievement bullets**, phrased
with the help of anonymized strong bullets retrieved from *other* employees' CVs (P1T-57…67).

### Why the feature is bullet-shaped (the gate verdict, P1T-58)

Before any plumbing was built, a throwaway prototype ran tailoring for 3 demo employees × one JD,
with and without hand-picked cross-CV bullets injected as labelled style exemplars carrying
distinctive fabrication tripwires. Findings: **honesty held** (zero tripwire facts leaked — the
framing that achieved it now ships verbatim in the agent's instructions: *"STYLE EXEMPLARS — bullets
from OTHER people's CVs… The candidate did NOT do these things. Imitate the writing QUALITY; NEVER
borrow their facts, numbers, systems, or achievements."*), but the lift for the then-current output
shape (summary + advisory guidance) was **marginal — not worth building retrieval for**. The output
that *would* benefit from quantified, cause→effect exemplar phrasing is rewritten achievement
bullets, which the agent never produced. Verdict: proceed, re-scoped to bullet rewriting.

### Achievement chunks stay out of employee-level search

The bullet chunks (see the data model) exist *only* for exemplar retrieval. When they first entered
the shared chunk pool, the **live eval gate caught the regression**: the negative-false-positive
rate went 0.0 → 0.1667 (an off-topic query pulled in an employee through a single bullet) and MRR
dipped 0.9848 → 0.9697. Filtering `SourceType = Achievement` out of the shared ranking query used by
both `roster_semantic_search` and `roster_shortlist_search` **restored every metric to the committed
baseline exactly** (recall@5 1.0, MRR 0.9848, neg-FP 0.0). Bullet narrative still reaches those
paths rolled into the parent experience chunk; the exemplar path below targets bullet chunks
exclusively.

### `style_exemplar_search` (MCP tool, `mcp:read`, read-only)

`style_exemplar_search(achievementIds[], topKPerBullet?)` → `IExemplarSearchService.SearchAsync`.
**Id-keyed by design**: the caller passes achievement GUIDs taken from `cv_get`, and the service
resolves each bullet's stored text server-side — the model never supplies free text to embed.
Unknown/empty ids are skipped silently. Then:

1. **One batched embed call** for all resolved bullets.
2. Per bullet, rank **other** employees' Achievement chunks by cosine similarity
   (`EmployeeId != owner` — a bullet never gets its own CV back), under the shared `MinSimilarity`
   floor (0.55) and a SQL length-band pre-filter.
3. **Quality gate** in memory (`ExemplarQualityFilter`): an exemplar must be *quantified* (contain a
   digit or `%` — the hallmark of strong CV phrasing) and sit inside the length band
   (`ExemplarMinChars` 40 – `ExemplarMaxChars` 300: shorter carries no style, longer is a paragraph).
4. **Dedup within the request**: the same source bullet is never returned twice, whichever requested
   bullet it matched first (closest first).
5. **Anonymization scrub** (`ExemplarAnonymizer`) before any text leaves the service: every
   occurrence of the source employee's first/last name → `[name]`, every employer's company name →
   `[company]` (case-insensitive, whole-word, longest term first).

`topKPerBullet` defaults to `ExemplarsPerBullet` (2), clamped to `ExemplarsPerBulletMax` (5) — all
under the `SemanticSearch` config section. Result: per requested bullet
`{ achievementId, exemplars: [{ text, similarity }] }`; a bullet with no strong match nearby gets an
empty list. If the embedding backend fails, the tool returns a **soft error**
(`{ results: [], error: "…" }`) — callers degrade, never fault.

### The 2-turn tailoring flow

`CvTailoringAgent` (tools narrowed to `cv_get` + `style_exemplar_search`) runs one 2-turn session:

- **Turn 1** — fetch the CV, select up to 8 JD-relevant achievement ids, call the exemplar tool
  exactly once, and produce the advisory markdown (rewritten summary + tailoring guidance) exactly
  as before — the answer stays **byte-compatible with the pre-rewrite contract**, and the model is
  instructed not to mention exemplars or the upcoming rewrites in it.
- **Turn 2** — driven by the agent class itself (a fixed `"Now the rewrites."` message, not the
  caller): the model replies with only minimal JSON `[{"achievementId","rewritten"}]`. Keeping the
  turn agent-side leaves turn 1 untouched and gives the rewrite step its own failure isolation.

Per-run `DelegatingAIFunction` wrappers (the shortlist capture pattern) record the `cv_get` result,
the achievement ids the model selected, and the exemplar payload.

**Endpoint composition** (`TailoringComposer`): `POST /agents/cv-tailoring` now returns the hybrid
contract

```json
{ "answer": "<markdown as before>",
  "rewrites": [{ "experienceId": "…", "achievementId": "…", "original": "…", "rewritten": "…" }] }
```

The answer is turn 1's markdown verbatim. Every deterministic rewrite field — `experienceId`,
`achievementId`, `original` — is resolved from the **captured cv_get result**, never from model text
(the Agents service may not query employee data directly; MCP is the boundary); the model's turn-2
JSON contributes only the `rewritten` strings. Entries with unknown/unselected/duplicate ids or
blank text are dropped; the JSON parse is lenient (tolerates prose and markdown fences).

### FabricationGuard

Pure, endpoint-side vetting of each surviving rewrite (P1T-65). Two conservative rules; a violation
**drops the rewrite and logs a warning — no retry**:

1. **Numbers-subset**: every numeric token in the rewrite (`40%`, `10x`, `4.5`, `90k`, `2020`, …)
   must already appear in the original bullet or its parent experience's context
   (company/title/period/summary — so dates and durations from the experience header are
   legitimate). A number the CV never stated is a fabrication, whatever its source.
2. **Exemplar-overlap**: no verbatim run of **8 words** (case-insensitive, punctuation-blind) shared
   with *any* style exemplar shown this run — whichever bullet it arrived for; a phrase borrowed
   across bullets is still borrowed. Exemplars may lend phrasing quality, never phrases.

### Degrade chain

Corruption always degrades to *fewer rewrites* — never to a failed request, never to fabricated ids
or originals:

- **Exemplar tool soft-errors or returns nothing** → the model is instructed to rewrite the selected
  bullets anyway, in the same spirit (and without a captured exemplar call, CV membership alone
  decides which ids are composable).
- **Rewrite-side failure** (turn-2 fault, unparseable JSON, guard drops everything) → **200,
  answer-only** (`rewrites: []`); the caller keeps the answer it already has.
- **Turn-1 / upstream failure** (MCP, auth, model) → **502**, as for the other agents.

### Tailor CV tab: rendering + Apply

The widget renders the rewrites as **before → after cards grouped by experience** (neutral
"Experience N" headers — the widget deliberately doesn't fetch the CV for a fancier header), each
with a copy button for the rewritten text and its own **Apply** button with strictly per-card
pending/applied/error state. **Exemplars are never disclosed in the UI** (or in the answer text).

**Apply authority model** (P1T-62/P1T-67): the agent never writes. Apply is a plain Web-API edit
with the **user's own session**, exactly like a manual edit — the SPA fetches the employee, swaps
the one bullet's text, and `PUT /api/experiences/{id}`s the experience back otherwise unchanged
(there is no per-achievement endpoint). That PUT **regenerates achievement ids**, so the apply hook
falls back from id → original-text match (a sibling rewrite was applied first) → rewritten-text
match (re-apply; the PUT becomes a no-op). Success invalidates the employee's detail/CV queries by
prefix so open views refetch.

**Live-verified** end-to-end: rewrites rendered in the tab, Apply persisted through the user's
session, the stale-id fallback exercised by applying sibling rewrites in sequence, and zero
agent-side writes observed.

---

## Retrieval evals

Retrieval quality is **measured, not vibed** (`tools/RetrievalEval.Core` + `tools/RetrievalEval`):

- **Frozen corpus** (24 hand-authored employees; keyword terms exclusive per employee) + **golden
  set** (39 labelled queries: keyword / paraphrase / cross-facet / negative). Fixtures live with the
  eval core; labels are versioned truth — never mix demo data in.
- **Metrics**: recall@5, MRR, negative-query false-positive rate (the trio the threshold trades
  between), plus keyword-subset recall@5 (feeds the hybrid question).
- **Live regression gate**: `dotnet test --filter "Category=live"` with `GEMINI_API_KEY` — real
  embeddings against Testcontainers pgvector, asserts no regression vs the committed floor.
- **Sweep CLI**: embeds once, re-ranks per threshold (a full sweep costs one run's embedding budget):
  `GEMINI_API_KEY=<key> dotnet run --project tools/RetrievalEval -- --sweep 0.30:0.80:0.05 --refine`.

**Measured baseline + standing verdicts** (see [`retrieval-eval-baseline.md`](retrieval-eval-baseline.md)):
at 0.30 (2026-07, retired OpenAI model) — recall@5 **1.0**, MRR **0.985**, negative-FP **0.0**, keyword recall **1.0**. Re-swept 2026-08-01 for `gemini-embedding-001`: plateau 0.540–0.575, all metrics perfect. Verdicts:
**`MinSimilarity` = 0.55 for Gemini** (was 0.30 for the OpenAI model — Gemini similarities cluster higher); **hybrid keyword+vector search not
adopted** (keyword gap 0.0 pts vs the >10-pt adoption rule) — the eval gate re-raises it if the gap
ever opens. Caveat: the small frozen corpus saturates recall by design; the gate guards regressions,
it doesn't claim perfection at scale.

---

## Demo data

500 synthetic employees across 10 industry clusters with rich career narratives
(`api/Infrastructure/Persistence/SeedData/demo-roster.json`, committed; all emails
`@demo.example.com`):

- **Generate/regenerate**: `tools/GenerateDemoRoster` — deterministic assembly from hand-authored
  career templates (seeded PRNG), optional LLM enrichment pass (`GEMINI_API_KEY`). The committed file
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
"Gemini":        { "Endpoint": "…", "EmbeddingModel": "gemini-embedding-001", "Dimensions": 1536, "ApiKey": "" },
"SearchIndex":   { "Enabled": true, "IntervalSeconds": 30, "EmbedBatchSize": 32 },
"SemanticSearch":{ "MinSimilarity": 0.55, "DefaultTopK": 5, "MaxTopK": 20,
                   "MaxSnippetsPerEmployee": 3, "SnippetMaxChars": 500,
                   "ShortlistDefaultTopK": 10, "ShortlistMaxTopK": 20,
                   // style exemplars (bullet rewriting):
                   "ExemplarsPerBullet": 2, "ExemplarsPerBulletMax": 5,
                   "ExemplarMinChars": 40, "ExemplarMaxChars": 300 }
```

Agents service `appsettings.json` (shortlist + tailoring + caps):

```jsonc
"McpAuth": { "shortlist":    { "ClientId": "agent-shortlist",     "Scope": "mcp:read", … },
             "cv-tailoring": { "ClientId": "agent-cv-tailoring",  "Scope": "mcp:read", … } },
"Usage":   { "DefaultDailyTokens": 50000, "DefaultWeeklyTokens": 150000, "DefaultMonthlyTokens": 500000 }
```

Web service `appsettings.json` (demo seeding, off by default):

```jsonc
"Seed": { "DemoRoster": false, "DemoRosterCount": null }
```

The embedding key is read from the `GEMINI_API_KEY` env var (preferred) or `Gemini:ApiKey`.
The worker is disabled in the in-memory MCP tests via `SearchIndex:Enabled=false`.

---

## Testing

- **Pure/unit** (`Application.Tests`): `ChunkProjection` (rendering, blank-summary skip, ordered
  achievements, hash stability, reorder-changes-hash), `Reconciler.Diff`
  (insert/no-op/update/delete/mixed), and `GeminiEmbedder` (batch shape, token count, logging)
  with a deterministic fake generator. Plus a skippable live embedding smoke test
  (`Category=live`, needs `GEMINI_API_KEY`).
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
- **Bullet rewriting** (`Application.Tests` + `Mcp.Tests` + `Agents.Tests` + `web`): pure exemplar
  quality gate + anonymizer scrub; pgvector `ExemplarSearchService` (owner exclusion, in-request
  dedup, similarity floor, quantified gate, soft error); `style_exemplar_search` transport binding;
  `FabricationGuard` rules; `TailoringComposer` (corruption guard, degrade paths); 2-turn agent
  capture + endpoint contract (exemplar soft error → rewrites still produced; unparseable turn 2 →
  answer-only 200; model fault → 502) + live smoke; Tailor CV tab component tests (grouped
  rendering, apply flow incl. the stale-id fallback).
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
- **Threshold**: measured per embedding model; 0.55 for `gemini-embedding-001` (mid-plateau 0.540–0.575, re-swept 2026-08-01), was 0.30 for the retired OpenAI model.
- **Hybrid keyword+vector search**: **not adopted** — keyword-subset recall showed zero gap. The
  pre-decided design (tsvector + GIN, `websearch_to_tsquery`, RRF k=60, transparent in
  `SemanticSearchService`) stays on record in the P1T-46 resolution; every future sweep's
  keyword-recall column re-raises the question automatically.
- **CV bullet rewriting**: shipped (P1T-57…67), see the bullet-rewriting section above. The gate
  prototype (P1T-58) measured summary-level exemplars as not worth building and re-scoped the
  feature to bullets; the eval gate caught the achievement-chunk leak into general search and the
  exclusion filter restored the committed baseline exactly. Live-verified end to end (rendering,
  Apply persistence, stale-id fallback, zero agent-side writes).

Remaining:

- **Secret hygiene**: never commit a real PAT — use `GEMINI_API_KEY`. (A PAT was found committed in
  `api/Agents/appsettings.json` during this work; rotate it.)
- **Scale**: revisit an HNSW index if the roster grows into the tens of thousands of chunks; rerun
  the sweep if the corpus or embedding model changes (recall saturates on the small frozen corpus).
- **Shortlist-specific evals** (requirement-extraction fidelity, coverage-merge ranking against
  labelled JDs): post-build follow-on, out of the original map's scope.
- **Multi-turn agent memory**: parked fog from the planning map (P1T-40), not started
  (CV-tailoring retrieval itself shipped as bullet rewriting).
