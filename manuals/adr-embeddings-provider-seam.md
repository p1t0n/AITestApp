# ADR: the embeddings provider is chosen by configuration, and switching it is safe

**Status:** accepted, 2026-09-27. Charted as Linear map EXP-52 ("Map: embeddings on Azure OpenAI —
Google out, provider selected by configuration"). Its decision tickets carry the evidence behind
every claim here, and are cited as `EXP-nn`. **Supersedes in part** `manuals/adr-chat-provider-seam.md`:
that ADR's §2 decision 3 assumed embeddings would keep calling Google whatever chat does, and they
no longer have to (§8). The build tickets are listed in §10; tickets 1 (`EXP-64`), 2 (`EXP-65`),
3 (`EXP-66`) and 4 (`EXP-67`) have landed. **Every decision in §2 is implemented**, and
**`AzureFoundry` is the shipped default in all three hosts** — on the default stack Google receives
no career narrative. Both construction branches exist, each provider carries its own similarity
floor, breaker window and key, a 429 honours `retry-after-ms` then `Retry-After`, a Production MCP
host refuses to boot without a key for its active provider while a development one degrades to
keyword matching, the AppHost injects both provider names and both keys into all three hosts, and
the eval and live gates run per provider on per-provider baselines. Ticket 5 (`EXP-68`) has brought
the compliance documents with it: the DPIA describes the four combinations, the former-recipient
entry, the Data Zone position and Google's researched terms; the chat ADR carries a supersession
note rather than a rewrite; and the agreement between the documents and the strings a data subject
reads is now a test (`ComplianceDocumentAgreementTests`) rather than a paragraph asking for it.
**All five tickets have landed.**

## The decision

Semantic roster search gets **its own provider seam**, `AddEmbeddingProvider(config)`, which reads
**`Ai:Embeddings:Provider`** and builds either today's Gemini embedder or an **Azure OpenAI**
`text-embedding-3-small` embedder. The setting is **independent of `Ai:Chat:Provider`**. **Azure is
the default**, and the Azure deployment runs on **`DataZoneStandard`, so embeddings are processed
within the EU**. **Gemini stays selectable for development and demo only.**

Switching provider can **never mix vectors from two models**. Each vector is tagged with the model
that made it, every search path compares only against the current tag, and the index reconciler
re-embeds whatever doesn't match. The privacy page names the provider actually in use, and names
**Google as a former recipient** to anyone whose record existed while Gemini was active.

## 1. Vocabulary

- **Embeddings provider**: the service that turns a career narrative into a vector. `Gemini` or
  `AzureFoundry`, per deployment, from `Ai:Embeddings:Provider`.
- **Tag**: `<provider>/<model>` (e.g. `AzureFoundry/text-embedding-3-small`), stored with each vector.
  The unit of "these two vectors are comparable".
- **Coverage**: chunks carrying the active tag with a vector, divided by all chunks. 1 means the index
  is fully on the current model.
- **Provider period**: one row of `EmbeddingsProviderPeriods`, the interval during which one
  embeddings provider was active on this deployment.

## 2. The decisions

### The seam `EXP-58`

1. **A separate `Ai:Embeddings:Provider`**, bound to `EmbeddingsProvider { Gemini, AzureFoundry }`,
   an enum in **Infrastructure** (Web, MCP and the eval tool all read it). **If the key is absent, it
   means `Gemini`**, the same as chat's code default, so a config that never mentions it keeps its old
   behaviour and a switch is always written down on purpose. The checked-in `appsettings.json` of Web,
   MCP and Agents sets `AzureFoundry`. **An unknown value throws at startup in every environment.**
2. **Each provider's embedding settings live in that provider's block** and share its `Endpoint` and
   `ApiKey` with chat:

   | Key | `Ai:Gemini` | `Ai:AzureFoundry` |
   |---|---|---|
   | `EmbeddingModel` | `gemini-embedding-001` | `text-embedding-3-small` (a **deployment name**) |
   | `Dimensions` | 1536 | 1536 |
   | `MinSimilarity` | **0.55** | **0.30** |
   | `QuotaBreakerSeconds` | 1800 | **60** |

   The global `SemanticSearch:MinSimilarity` is **removed**, and a leftover one **throws at startup**
   naming the new keys.
3. **Each provider reads only its own key**: Azure `AZURE_FOUNDRY_API_KEY`, then
   `Ai:AzureFoundry:ApiKey`; Gemini `GEMINI_API_KEY`, then `Ai:Gemini:ApiKey`. Today's code reads
   `GEMINI_API_KEY` first whatever the endpoint, so a request aimed at Azure would carry the Google key
   (`EXP-53`). That can't happen after this.
4. **Every host knows both providers.** The AppHost sets `Ai__Chat__Provider` and
   `Ai__Embeddings__Provider` **once** and injects them into Web, MCP and Agents, and passes **both**
   key parameters to the MCP host explicitly. A test asserts that the three hosts' checked-in
   `appsettings.json` agree on both providers, which covers solo `dotnet run` and deployments. The Web
   host had already drifted from Agents on `Ai:Chat:Provider` (EXP-61); this closes that class of bug.
5. **Names:** `AddGeminiEmbeddings` becomes `AddEmbeddingProvider(config)`; `GeminiEmbedder` becomes
   `OpenAICompatibleEmbedder`. It never was Gemini-specific.
6. **Missing credentials** mirror chat. A **Production** MCP host refuses to start without a key for
   the active embeddings provider. **Development** starts, and semantic search falls back to keyword
   matching.
7. **Retry and breaker.** On a 429, retry honours **`retry-after-ms`, then `Retry-After`**, then the
   existing 20/40/60s waits. The breaker opens for the active provider's `QuotaBreakerSeconds`. While
   it's open, query-time search drops to keyword matching, so on Azure that's at most a minute.
   Azure's limit is tokens per minute (`x-ratelimit-limit-tokens`), not a daily cap.
8. **The retrieval eval and live gates follow the provider.** `tools/RetrievalEval` reads `Ai:*` (with
   a `--provider` override). `EvalBaselines` holds one floor per provider. `RetrievalEvalLiveTests` and
   `EmbeddingLiveSmokeTests` run per provider, each skipping when its own key is missing.

### Switch safety `EXP-59`

9. **The tag is `<provider>/<model>`**, written to the **existing** `ExpertSearchChunk.Model` column,
   so there's no schema change. A bare model name could collide, because Azure's value is a deployment
   name someone chooses. It is formed in one place, `OpenAICompatibleEmbedder.TagFor`, and reaches every
   caller as `IEmbedder.Tag`; that interface's default (the bare model) is what an embedder with no
   provider behind it honestly is, and is the shape decision 12 recognises as legacy.
10. **All three search paths** (semantic search, shortlist, exemplar) filter on `Model = <active tag>`
    next to `Embedding IS NOT NULL`. Comparing across models becomes impossible, not merely unlikely.
11. **The reconciler re-embeds** any chunk whose tag differs from the active one, through its existing
    batch-of-32 loop and quota breaker. Old vectors are **overwritten in place, never blanked in bulk**,
    so a slow or failed re-embed never empties search.
12. **Existing rows.** Rows whose bare `Model` equals the active embedder's model are **relabelled**
    into the tagged form; rows with any other bare name count as **stale**. That clears the leftovers
    of the earlier GitHub Models → Gemini switch (P1T-88, which shipped no re-embed and left a mixed
    index) *without* forcing a full re-embed on Gemini's free tier in the interim step (a revision
    recorded on `EXP-59`).
13. **Mid-switch, search returns what's re-embedded, plus a coverage note** in the tool result (e.g.
    "index rebuilding: 43% re-embedded"), so the agent can say the answer may be incomplete. The
    reconciler also publishes an OpenTelemetry gauge, **`experttojob.search.index.coverage`** (0–1).
    There's no keyword-only fallback and no hybrid search; hybrid search was decided against on the
    benchmark (`manuals/retrieval-eval-baseline.md`, P1T-54).
14. **GDPR erasure needs no change.** A vector lives on the same row as its text, and erasure deletes
    the row, whichever model made it. A test pins this.
15. **Pace.** The demo roster is 8,298 chunks, or 260 requests (`EXP-54`): **minutes on Azure**, about
    **3 days on Gemini's free tier**. Seeding the demo roster needs nothing special: the reconciler
    embeds new rows with the current tag, and `--wipe` deletes rows along with their vectors.

### Disclosure `EXP-62`

16. **Recipients follow both providers.** The Art. 15 list covers four present-tense combinations:

    | Chat · Embeddings | Entries |
    |---|---|
    | Gemini · Gemini | "Google (Gemini), as our AI model provider" |
    | Azure · Azure | "Microsoft (Azure OpenAI), as our AI model provider" |
    | Azure · Gemini | "Google (Gemini), as our embeddings provider" + "Microsoft (Azure OpenAI), as our AI model provider" |
    | Gemini · Azure | "Microsoft (Azure OpenAI), as our embeddings provider" + "Google (Gemini), as our AI model provider" |

    Where Microsoft handles embeddings, the entry says they are **processed within the EU**.
17. **Past recipients are tracked per deployment** in `EmbeddingsProviderPeriods` (provider, started at,
    ended at), which holds no personal data. The **MCP host** writes it at startup whenever the active
    provider differs from the latest row. The migration seeds **one open Gemini row with an empty start
    date**, meaning "since the beginning".
18. **An Expert created before a Gemini period ended sees Google as a former recipient:** "Until
    {date}, your career narrative was sent to Google's Gemini models to be turned into search
    embeddings. Since then, embeddings are produced by another provider named on this page, and Google
    receives nothing new from us." The entry states only verifiable facts, **nothing about what Google
    retained**. Google's terms go in the DPIA.
19. **No real person's data has been sent to Google.** Every deployment so far holds only synthetic
    demo data and dev data, so there is no retroactive notice to send.

## 3. What was measured

- **The deployment** (`EXP-56`): `text-embedding-3-small` version 1 on **`DataZoneStandard`**,
  capacity 120, in `experttojob-openai-swc` (Sweden Central). A v1 request with the SDK's bearer key
  and `dimensions: 1536` returned HTTP 200 and a 1536-dimension vector. The standing cost is $0.
- **Retrieval quality** (`EXP-57`), on the frozen corpus of 24 experts and 39 golden queries, through
  the real reconciler, pgvector and search service:

  | | Azure `text-embedding-3-small` | Gemini `gemini-embedding-001` |
  |---|---|---|
  | Plateau (recall 1.0, 0 false positives) | 0.285–0.350 | 0.540–0.575 |
  | Recall@5 / MRR on the plateau | 1.0000 / 0.9848 | 1.0000 / 1.0000 |
  | Recall@5 at 0.55 | **0.3030** | 1.0000 |

  The Azure numbers are identical row for row to the 2026-07-11 baseline, which measured the same
  model through GitHub Models. Same model, same vectors. The last row is why the threshold is per
  provider: Gemini's value on Azure vectors hides 70% of the correct matches.
- **Rate limits:** 200 concurrent requests all returned 200. Azure advertised only
  `x-ratelimit-limit-tokens: 120000`. A 429 was never observed, so its header shape is designed for
  (decision 7), not verified.

## 4. Why a separate provider key, not one for both

The two backends are genuinely independent (one runs in Agents, the other in MCP), and the
disclosure already has to describe mixed deployments. One key would force chat and embeddings to move
together, and would turn "switch chat back to Gemini to compare" into "and re-embed the whole index".

## 5. Why Gemini is development and demo only

Google's terms (`EXP-63`, fetched 2026-09-27) make free-tier Gemini unsuitable for real people. Only
paid services may be used when serving users in the EEA, UK or Switzerland. Outside those areas, free
use lets Google use content to improve its products, allows human review, states no retention period,
and says not to submit personal information. Paid use is covered by Google's data processing
agreement and logged for 55 days for abuse monitoring. A deployment holding real people therefore uses
Azure, or a **billed** Gemini project as a new, separate decision. The seam can't enforce this, so the
DPIA records it — `manuals/dpia-expert-workspace.md` §1, in the transfers table and as a review
trigger, with the full terms alongside (EXP-68).

## 6. Why the reconciler re-embeds, not a migration job

The reconciler already walks every chunk and embeds anything without a vector. Treating "wrong tag"
like "no vector" heals every mismatch, however it arose (a switch, a restored backup, the P1T-88
leftovers), with no new moving parts. A one-off job would heal only the switch it was written for.

## 7. Residency

- **Embeddings: EU-confined.** Data Zone quota for `text-embedding-3-small` turned out to be 1000 on
  this subscription (`EXP-56`). The map had assumed 0, carried over from chat, where it's still true
  for `gpt-4.1-mini`. EU processing costs 10% more, about $0.001 per full roster embed.
- **Chat: not EU-confined.** It stays on `GlobalStandard` (chat ADR §7). DPIA risk R7 remains open for
  chat and is outside this ADR. After this change, the only outside AI recipient on the default stack
  is Microsoft.

## 8. What this supersedes

- **`manuals/adr-chat-provider-seam.md` §2 decision 3** ("embeddings keep calling Google whatever chat
  does") and the matching comment on the `ChatProvider` enum. The chat ADR gets a "superseded in part"
  note; it isn't rewritten.
- **That ADR's out-of-scope item "a second embeddings provider"**, which named this exact effort.
- **The global `SemanticSearch:MinSimilarity`** and its single Gemini-calibrated value.

## 9. What would make us revisit

- **Chat gets Data Zone quota.** Then both paths could be EU-confined, and R7 could close entirely.
- **A different embedding model or dimension** (`text-embedding-3-large`, anything not 1536). That's a
  schema migration, out of scope here.
- **Real use of Gemini** becomes necessary. That needs a billed project and a DPIA entry first.
- **A 429 is finally observed on Azure.** Check whether `Retry-After` is present, and simplify the
  retry if so.
- **The corpus outgrows the frozen benchmark.** The 24-expert benchmark saturates recall by design, so
  `MinSimilarity` 0.30 means "not measurably improvable here", not "optimal at scale".
- **Entra ID replaces API keys** on both paths (fog carried over from the chat map).

## 10. The build tickets

Five tickets, `ready-for-agent`, ordered by `blockedBy`. The Azure default is deliberately last among
the code tickets: it must not ship onto a mixed index or behind a wrong disclosure.

| # | Ticket | | Blocked by |
|---|---|---|---|
| 1 | Embeddings seam with Gemini as the only provider: AddEmbeddingProvider, per-provider settings and keys | `EXP-64` ✅ | — |
| 2 | Safe embeddings provider switches: provider/model vector tags, tag-filtered search, in-place re-embed, coverage note | `EXP-65` ✅ | 1 |
| 3 | Art. 15 recipients follow both providers, with a former-recipient entry from the embeddings provider history | `EXP-66` ✅ | 1 |
| 4 | Azure OpenAI embeddings, made the default: construction branch, per-provider retry and breaker, AppHost wiring, live gates | `EXP-67` ✅ | 1, 2, 3, `EXP-61` |
| 5 | Compliance documents follow embeddings to Azure: DPIA, provider prose, README and CLAUDE.md | `EXP-68` ✅ | 4 |

`EXP-61` ("Privacy page names Google as the AI model provider while chat runs on Azure") is a live
bug fixed separately. It does the chat half of decision 4, which ticket 4 extends.

The literal test names live in each ticket's acceptance criteria.
