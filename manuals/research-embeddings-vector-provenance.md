# Research: vector provenance and what the reconciler does when the embedding model changes

Resolves **EXP-54** for the map **EXP-52** ("embeddings on Azure OpenAI, Google out, provider
selected by configuration"). Every answer comes from this repository at `86850c7` (main,
2026-09-27). Citations are `path:line`. The one figure taken from outside the code, the Azure
`text-embedding-3-small` rate limit, is quoted from `manuals/azure-foundry-model-and-cost.md`,
which already cites Microsoft Learn for it.

## TL;DR

1. **A chunk does record its model, but only the model id.** `ExpertSearchChunk.Model` is a
   `varchar(200)` holding `IEmbedder.Model`, for example `"gemini-embedding-001"`. There is no
   provider column and no dimensions column; dimensions are fixed by the `vector(1536)` column
   type. **Nothing reads `Model` back.** It is written in two places and never compared or filtered.
2. **The reconciler notices content changes, never model changes.** A chunk gets re-embedded only
   when it is new, when its content hash changed, or when its `Embedding` is null. A model switch
   leaves every existing vector in place, and only edited or new chunks pick up the new model.
   The index ends up mixed with no signal that it is.
3. **The search path skips chunks with no vector and cannot recognise a stale one.** Its only
   filter is `c.Embedding != null`, so it compares a new-model query vector against old-model
   chunk vectors without complaint. `MinSimilarity` (0.55, calibrated on Gemini) is applied as a
   SQL cosine-distance cutoff in `SemanticSearchService.RankChunksAsync` and in both
   `ExemplarSearchService` paths.
4. **Throughput.** A full re-embed of the demo roster is about **8,300 chunks, or 260 requests** at
   the batch size of 32, and roughly **400k input tokens** (a chars/4 estimate). On **Azure** Tier 0
   limits it is a single reconcile pass lasting minutes, with rate limits nowhere near binding. On
   the **Gemini free tier** it takes **about 3 days** if the "100/day" cap counts requests, or
   **about 83 days** if it counts inputs. The codebase never says which unit the cap uses.
5. **Other things keyed to the model:** the retrieval eval (tool, live test and committed
   baselines), `MinSimilarity` and its eval default, `EmbeddingLiveSmokeTests`, and
   `EmbedderTests`' 1536 dimension pin. **No recorded real vectors exist in the repo.** Every test
   embedder is a synthetic 1536-dim fake.

A precedent worth knowing: the last model switch (GitHub Models `text-embedding-3-small` to
`gemini-embedding-001`, commit `f1c3440`, P1T-88) shipped **no re-embed mechanism**. It kept the
dimensions at 1536 so the schema stayed untouched, retuned `MinSimilarity`, and relied on the
databases of the day being rebuilt. The commit message says nothing about existing vectors.

---

## 1. What a stored chunk records

| Field | Where | What it holds |
|---|---|---|
| `Embedding` | `api/Infrastructure/Persistence/ExpertSearchChunk.cs:39-40` | `Vector?`, "1536-dim embedding of Content; null until embedded" |
| `Model` | `ExpertSearchChunk.cs:42-43` | `string`, "Embedding model id used, e.g. "gemini-embedding-001". Empty until embedded." |
| `EmbeddedAt` | `ExpertSearchChunk.cs:45-46` | `DateTimeOffset?` |
| `ContentHash` | `ExpertSearchChunk.cs:36-37` | SHA-256 of `Content`, "drives dirty detection" |

Schema:

- EF mapping: `Model` has `HasMaxLength(200)` and `Embedding` has `HasColumnType("vector(1536)")`
  (`api/Infrastructure/Persistence/AppDbContext.cs:393`, `:397`). Under non-Npgsql providers
  `Embedding` is ignored (`:399-403`).
- Migration: `Model = varchar(200) NOT NULL`, `Embedding = vector(1536) NULL`
  (`api/Infrastructure/Persistence/Migrations/20260706042310_AddEmployeeSearchChunk.cs:28-29`).
  No later migration touches either column. The table was renamed from `EmployeeSearchChunks` in
  `20260901062319_RenameEmployeeToExpert`.
- **There is no vector index** (HNSW or IVFFlat). The only indexes are the unique
  `(SourceType, SourceId)` and `ExpertId` (`AppDbContext.cs:406-408`). This is the planned "no ANN
  index v1 (flat scan)" (`manuals/rag-semantic-roster-search-plan.md:20`). A re-embed therefore
  has no index to rebuild.

**What is recorded:** the model id string, as `IEmbedder.Model` reports it. The interface
documents it as "Stamped onto chunks" (`api/Application/Abstractions/IEmbedder.cs:10-11`).
`GeminiEmbedder.Model` is simply the configured `EmbeddingModel`
(`api/Infrastructure/Embeddings/GeminiEmbedder.cs:36`, `:44`;
`EmbeddingServiceCollectionExtensions.cs:38`).

**What is not recorded:**

- **Provider or endpoint.** Nothing stores where the vector came from. Both an Azure deployment
  and OpenAI-direct could report `text-embedding-3-small`, and so could a deployment name that
  happens to match. For Azure the `model` string is a *deployment name*: whether it means that for
  embeddings is EXP-53's question. So the stored id might be a deployment alias and not a model
  identity.
- **Requested dimensions.** The request pins `Dimensions` (`GeminiEmbedder.cs:55`, default 1536 at
  `EmbeddingOptions.cs:24`). Only the column type records it, and pgvector rejects a vector of the
  wrong length on write.

**Who writes `Model`, and who reads it.** The only writes are
`SearchIndexReconciler.cs:160` (`batch[i].Model = _embedder.Model`) and `:105` (cleared to empty
when content changes). A repo-wide search for `.Model` on chunks turns up no read in any query,
filter, diff or test beyond `SearchIndexReconcilerTests.cs:47`, which asserts the stamp is written
(`c.Model == "fake-embedder"`). The diff input `ExistingChunk` doesn't carry `Model` at all
(`api/Application/Search/SearchChunkModels.cs:21-25`; the projection is at
`SearchIndexReconciler.cs:85-87`).

## 2. What `ReconcileWorker` / `ISearchIndexReconciler` reconcile

`ReconcileWorker` is only the loop. It runs one `RunOnceAsync` per tick in a fresh DI scope
(`api/Mcp/Search/ReconcileWorker.cs:41-78`). It is registered in the MCP host
(`api/Mcp/Program.cs:57-59`). Timing:

- Every `IntervalSeconds` (30) after a success or a generic failure
  (`ReconcileWorker.cs:84-87`; `api/Mcp/appsettings.json:18-23`).
- `QuotaBackoffSeconds` (1800) after an `EmbeddingQuotaExceededException` (`ReconcileWorker.cs:54-62`).
- `Enabled=false` stops the loop before it starts (`ReconcileWorker.cs:31-35`).

`SearchIndexReconciler.RunOnceAsync` runs two phases (`api/Infrastructure/Search/SearchIndexReconciler.cs:53-67`).

**Phase 1, sync (`:69-135`).**

1. Loads every **Active** expert with their experiences and achievements (`:73-78`).
2. Projects them to desired chunks with `ChunkProjection.Project`: one Summary chunk, one chunk
   per Experience with the bullets rolled in, and one per non-blank Achievement
   (`api/Application/Search/ChunkProjection.cs:25-50`).
3. Diffs against persisted chunks with the pure `Reconciler.Diff`
   (`api/Application/Search/Reconciler.cs:13-41`). Chunks match on `(SourceType, SourceId)`:
   - **Upsert** when the source key is new (`:28-31`) or the **content hash differs** (`:22-26`).
   - **Delete** when the source key is no longer desired (`:34-38`). That covers removed rows and
     experts who left the Active set (`SearchIndexReconciler.cs:71-72`).
4. An upsert on an existing row resets `Content`, `ContentHash`, `Embedding=null`, `Model=""` and
   `EmbeddedAt=null` (`:99-107`). A new row starts with `Embedding=null` (`:111-121`).

**Phase 2, embed (`:137-169`).** Selects **every** chunk where `Embedding == null` (`:139-141`) and
embeds them in batches of `EmbedBatchSize`, default 32 (`:152`; `SearchIndexOptions.cs:17-18`).
It stamps the vector, `Model` and `EmbeddedAt` (`:157-163`) and calls `SaveChangesAsync` **after
each batch** (`:165`). If a batch fails partway through a pass, the batches before it stay
committed and the rest stay null for the next pass. One pass drains the whole backlog, with no
pacing between batches.

**What triggers a re-embed today:**

- a new chunk (new source row, or an expert becoming Active)
- a content-hash change (any edit that changes the rendered text)
- `Embedding` being null for any other reason, such as a manual `UPDATE … SET "Embedding" = NULL`
  or a truncate. The entity doc says the table "can be truncated and regenerated at any time"
  (`ExpertSearchChunk.cs:9-10`).

**What never triggers a re-embed:**

- a change of `IEmbedder.Model`
- a change of provider, endpoint or dimensions config
- a change to `MinSimilarity`

The chunk's `Model` is never compared with `_embedder.Model` anywhere.

**So after a model switch** every existing vector stays and keeps its old `Model` stamp. Only
chunks edited or added after the switch get new-model vectors, and the index drifts into a mix
with nothing to flag it. Today the **only way to force a full re-embed** is out of band: null the
`Embedding` column or truncate the table, then let the worker backfill. No code, command or test
does this.

## 3. The search path with missing or stale vectors, and where `MinSimilarity` applies

**Missing vector (`Embedding IS NULL`): skipped, silently.**

- Expert search and shortlist ranking: `RankChunksAsync` filters `c.Embedding != null`
  (`api/Infrastructure/Search/SemanticSearchService.cs:315-316`). The shortlist path reuses it per
  requirement (`:162-164`).
- Exemplar search, both paths: `c.Embedding != null` (`api/Infrastructure/Search/ExemplarSearchService.cs:114`, `:173`).

An expert whose chunks are all still pending just doesn't appear, with no error and no degraded
flag. Partway through a backfill, results are simply incomplete.

**Stale vector (produced by a different model): used as if current.** No path filters on `Model`.
The query is embedded with the *current* embedder (`SemanticSearchService.cs:55`, `:145`;
`ExemplarSearchService.cs:93`, `:159`) and compared by `CosineDistance` against whatever vector is
stored (`SemanticSearchService.cs:326`; `ExemplarSearchService.cs:127`, `:182`). Similarities
across two models' vector spaces are meaningless, but at the same dimensions pgvector computes them
without error. The results are therefore wrong without anything failing.

(A *dimension* mismatch would fail loudly: a Postgres error from `CosineDistance`, thrown outside
the embed `try` at `SemanticSearchService.cs:69-75` and reaching `McpToolExecutor.RunAsync`,
`api/Mcp/McpToolExecutor.cs:24-37`. EXP-52 settles 1536 on both sides, so this case is out of
scope.)

**Embedding failures, by path:**

| Path | Quota exhausted | Other embed error | Where |
|---|---|---|---|
| `roster_semantic_search` (`SearchAsync(query)`) | **Lexical fallback.** Postgres FTS over the same chunk pool, flagged degraded. It does *not* filter on `Embedding`, so pending chunks still match | soft `Failed("…unavailable.")` | `SemanticSearchService.cs:58-67`, `:219-238` |
| shortlist (`SearchAsync(requirements)`) | soft `Failed` (no lexical fallback) | soft `Failed` | `:148-152` |
| exemplar (bullets / theme) | soft `Failed` | soft `Failed` | `ExemplarSearchService.cs:96-100`, `:162-166` |

**`MinSimilarity` is applied** as `maxDistance = 1.0 - MinSimilarity` in SQL, with
`WHERE distance <= maxDistance`:

- `SemanticSearchService.RankChunksAsync`, at `SemanticSearchService.cs:309` and `:327`. It covers
  expert search and shortlist.
- `ExemplarSearchService`, at `ExemplarSearchService.cs:102` and `:168`.

It comes from `SemanticSearchOptions.MinSimilarity`, code default 0.55
(`api/Infrastructure/Search/SemanticSearchOptions.cs:8-13`, whose doc comment says "Tuned per
embedding model"), bound from `SemanticSearch:MinSimilarity` = 0.55 (`api/Mcp/appsettings.json:24-25`).
There is one global value: it is not per provider or per model, and not tied to the stamped `Model`.

## 4. Throughput limits and full re-embed duration

### Limits in code

| Limit | Value | Where |
|---|---|---|
| Batch size (inputs per provider call) | 32 | `SearchIndexOptions.cs:17-18`; `api/Mcp/appsettings.json:21` |
| Retries on 429 | 4 attempts, delays 20s, 40s, 60s (120s total) | `GeminiEmbedder.cs:39`, `:75-96` |
| Quota breaker (embedder) | after the 4th 429 it opens for `QuotaBreakerSeconds` = 1800s, failing fast | `GeminiEmbedder.cs:84-88`, `:103-124`; `EmbeddingOptions.cs:29-32` |
| Worker backoff after quota | `QuotaBackoffSeconds` = 1800s | `ReconcileWorker.cs:54-62`, `:84-87`; `SearchIndexOptions.cs:20-23` |
| Pacing between batches inside a pass | none | `SearchIndexReconciler.cs:152-166` |

Nothing in the breaker is Gemini-specific beyond its name. It keys on HTTP 429 from the OpenAI SDK
(`ClientResultException … Status == 429`, `GeminiEmbedder.cs:82`). Whether Azure's 429s look the
same is EXP-53's question.

The breaker window and the worker backoff are both 1800s. Because the worker's wait starts just
after the breaker opens, the breaker has usually expired by the next pass. On a daily cap, each
half-hour the worker spends up to 4 more requests on 429s before it backs off again. Whether a
rejected 429 counts against the cap is not determined here.

### Workload, measured from the committed dataset

Counted from `api/Infrastructure/Persistence/SeedData/demo-roster.json` with the projection rules
in `ChunkProjection.cs:25-50`:

| Chunk type | Count |
|---|---|
| Summary (non-blank) | 500 |
| Experience | 1,741 |
| Achievement (non-blank) | 6,057 |
| **Total** | **8,298** |

The base seed adds 3 experts (`api/Infrastructure/Persistence/DbInitializer.cs:62-66`), which is
negligible. All seeded experts default to `Active` (`api/Domain/Entities/Expert.cs:25`), so all are
indexed.

- **Requests:** ceil(8,298 / 32) = **260** embedding calls.
- **Input tokens:** about **400k** (assumption: 1.6M characters of embedded text, since bullet text
  appears twice, once alone and once inside its experience chunk; divided by 4 characters per
  token). The chars/4 ratio is a heuristic, not a measurement. `GeminiEmbedder` logs the real
  billed count per batch (`GeminiEmbedder.cs:58-62`).

### Azure OpenAI `text-embedding-3-small`, `GlobalStandard`

Quoted limits (Tier 0): **1,000 requests per 10s, 1,000,000 TPM**
(`manuals/azure-foundry-model-and-cost.md:287`, citing Microsoft Learn quotas-limits).
**Assumption:** this subscription is on Tier 0. The manual says to check the tier
(`azure-foundry-model-and-cost.md:315-319`), and nothing here confirms it. Higher tiers only
loosen the limits. EXP-53 owns the authoritative figures.

- Request bound: 260 requests against 1,000 per 10s gives about 3s even if all were sent at once.
  Not binding.
- Token bound: 400k tokens against 1M per minute gives about 0.4 min. Not binding.
- **Real bound: sequential latency.** The reconciler sends batches one after another in one pass.
  **Assumption:** 0.3 to 1.0s per 32-input call. That gives 260 × 0.3s ≈ 1.3 min up to
  260 × 1.0s ≈ 4.3 min, plus one `SaveChangesAsync` per batch.
- **Estimate: one reconcile pass, a few minutes.** Cost at $0.02 per 1M tokens (list price, an
  assumption and not verified here) is about $0.008.

### Gemini `gemini-embedding-001`, free tier

The codebase and manuals quote the cap only as "100/day" (`manuals/expert-visibility.md:58`;
`SemanticSearchService.cs:231`; EXP-52 notes say "About 100 embeddings a day"). **They never say
whether it counts requests or inputs**, and I did not determine it.

- If it is 100 **requests** per day: 260 / 100 means **about 2.6 days**, so 3 calendar days. Each
  day the cap trips, the breaker and backoff hold the worker for 30-minute windows, and the next
  UTC day resumes. That assumes a daily reset, which the code doesn't model.
- If it is 100 **inputs** per day: 8,298 / 100 means **about 83 days**. In practice the batch of 32
  would also have to fit, so this reading is effectively unusable for a full rebuild.
- Per-minute throttles: the retry comment says the free tier "throttles embedding requests per
  minute" (`GeminiEmbedder.cs:67-71`), but no RPM or TPM figure appears in the repo.

Either way, the historical claim that seeding the demo roster "takes days" (EXP-52 notes) is
consistent with the request reading.

### Behaviour during the re-embed

While a re-embed runs:

- Chunks still null are invisible to vector search (§3).
- Chunks whose vector is still old-model are compared against new-model query vectors, so results
  are wrong without any error (§3).

Under Azure this window lasts minutes. Under Gemini it lasts days, which is why nulling everything
at once costs you days of partial search there.

## 5. Everything else keyed to the embedding model

| Item | Model coupling | Where |
|---|---|---|
| `MinSimilarity` 0.55 | calibrated for `gemini-embedding-001` (was 0.30 for `text-embedding-3-small`) | `SemanticSearchOptions.cs:8-13`; `api/Mcp/appsettings.json:25`; `manuals/semantic-roster-search.md:387-388`, `:498` |
| Retrieval eval CLI | hard-codes Gemini endpoint + `gemini-embedding-001`, requires `GEMINI_API_KEY`, registers via `AddGeminiEmbeddings` | `tools/RetrievalEval/Program.cs:35-39`, `:63-75` |
| Eval runner default threshold | reads the **code default** `new SemanticSearchOptions().MinSimilarity`, not config | `tools/RetrievalEval.Core/EvalRunner.cs:55` |
| Eval report | prints `embedder.Model` into the report header | `tools/RetrievalEval/Program.cs:79`, `:107`; `tests/Mcp.Tests/Eval/MarkdownReportTests.cs:14`, `:42` |
| Live regression gate | `RetrievalEvalLiveTests`, `[Trait("Category","live")]`, skips without `GEMINI_API_KEY`, hard-codes Gemini config | `tests/Mcp.Tests/Eval/RetrievalEvalLiveTests.cs:23`, `:33-35`, `:64-78` |
| Committed baselines | "Measured 2026-08-01 with gemini-embedding-001": recall@5 1.0, tolerance 0.05 | `tests/Mcp.Tests/Eval/EvalBaselines.cs:7-20` |
| Benchmark doc | two sweeps, `text-embedding-3-small` @0.30 (2026-07-11) and `gemini-embedding-001` @0.55 (2026-08-01), over the frozen 24-expert corpus and 39-query golden set; says to rerun "if the corpus or embedding model changes" | `manuals/retrieval-eval-baseline.md:24-27`, `:73-76`, `:105-126` |
| Embedding live smoke | `EmbeddingLiveSmokeTests`, `Category=live`, Gemini-only, asserts a 1536-length vector | `tests/Application.Tests/EmbeddingLiveSmokeTests.cs:16-43` |
| Unit dimension pin | `EmbedderTests` asserts the request carries `Dimensions == 1536` and `Model == "gemini-embedding-001"` | `tests/Application.Tests/EmbedderTests.cs:21-31` |
| Tool-selection eval | `Category=eval` and `live`; uses a Gemini **chat** client, not embeddings | `tests/Mcp.Tests/Eval/ToolSelectionEvalTests.cs:174-191` |
| Config section | embeddings bind `Ai:Gemini` (`Endpoint`, `EmbeddingModel`, `Dimensions`, `ApiKey`, `QuotaBreakerSeconds`); key from `GEMINI_API_KEY` first | `EmbeddingOptions.cs:14-32`; `EmbeddingServiceCollectionExtensions.cs:21-33`; `api/Mcp/appsettings.json:11-17` |
| Compliance declarations | reference the embeddings recipient (EXP-55's scope, not examined here) | `api/Application/Compliance/PersonalDataDeclaration.cs`, `Art15Disclosure.cs` |

**Fixtures and recorded vectors.** The eval fixtures (`tools/RetrievalEval.Core/Fixtures/eval-corpus.json`,
`golden-set.json`) hold text only, with no vectors. Every in-test embedder is a synthetic
deterministic 1536-dim fake:

- `FakeEmbedder`: `SearchIndexReconcilerTests.cs:209-228`
- `KeywordEmbedder`: `SemanticSearchServiceTests.cs:191-203`, `RosterVisibilityTests.cs:325-337`, `EvalRunnerTests.cs:134-146`
- `CountingKeywordEmbedder`: `ExemplarSearchServiceTests.cs:309-326`, `ShortlistSearchServiceTests.cs:209-226`
- throwing and quota-dead variants alongside those

**No test pins a real model's vector or a real similarity value.** A model switch breaks only the
live and eval tests and the `EmbedderTests` literal. The `1536` array sizes in the fakes stay valid.

**Demo roster seeding** doesn't embed. It relies on the worker (`tools/SeedDemoRoster/Program.cs:8-9`).
How that interacts with a re-embed is listed as "Not yet specified" on EXP-52.

## What can't be determined from the codebase

- The unit of Gemini's "100/day" free-tier cap (requests or inputs), and its reset time.
- This subscription's Azure quota tier, and so its actual embedding RPM and TPM (EXP-53).
- Real per-call latency for either provider, and real token counts for the demo corpus (the
  chars/4 figure above is an estimate).
- Whether a 429 that the breaker's probes spend counts against Gemini's daily cap.
- How existing deployments' chunk tables were handled at the P1T-88 switch. The commit is silent,
  and the only safe assumption is that nobody re-embedded them.
