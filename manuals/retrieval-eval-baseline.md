# Retrieval eval baseline (P1T-52)

Measured retrieval quality of the semantic roster search against the frozen eval corpus
(`tools/RetrievalEval.Core/Fixtures/eval-corpus.json`) and golden set
(`tools/RetrievalEval.Core/Fixtures/golden-set.json`), embedded with the real production
pipeline (`SearchIndexReconciler` → pgvector → `SemanticSearchService`).

The committed regression floor lives in `tests/Mcp.Tests/Eval/EvalBaselines.cs` and is asserted
by `RetrievalEvalLiveTests` (`dotnet test --filter "Category=live"`).

> **`MinSimilarity` is per provider, and so is the floor** (EXP-67). Each threshold below was
> measured against one embedding model and is meaningless against another: **0.30** for
> `text-embedding-3-small` (Azure OpenAI, the shipped default) and **0.55** for
> `gemini-embedding-001`. Applying Gemini's 0.55 to Azure's vectors scores recall@5 **0.3030** — it
> hides 70% of the correct matches — which is why there is no single global number any more and why
> `EvalBaselines` holds one floor per provider, each quoted with the threshold it was taken at.
> `RetrievalEvalLiveTests` runs once per provider and each run **skips on its own missing key**
> (`AZURE_FOUNDRY_API_KEY`, `GEMINI_API_KEY`), so the set that actually runs is the set you have
> keys for.

## Baseline at the production threshold (0.30)

| Metric | Value |
|--------|-------|
| Recall@5 | 1.0000 |
| MRR | 0.9848 |
| Negative FP rate | 0.0000 |
| Keyword-subset recall@5 | 1.0000 |

## Threshold sweep

## Retrieval eval sweep

- Embedding model: `text-embedding-3-small`
- Corpus size: 24 experts
- Golden set: 39 queries
- Date: 2026-07-11

| Threshold | Recall@5 | MRR | Negative FP rate | Keyword recall@5 |
|-----------|----------|-----|------------------|------------------|
| 0.150 | 1.0000 | 0.9848 | 1.0000 | 1.0000 |
| 0.200 | 1.0000 | 0.9848 | 0.8333 | 1.0000 |
| 0.250 | 1.0000 | 0.9848 | 0.5000 | 1.0000 |
| 0.275 | 1.0000 | 0.9848 | 0.1667 | 1.0000 |
| 0.280 | 1.0000 | 0.9848 | 0.1667 | 1.0000 |
| 0.285 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.290 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.295 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.300 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.305 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.310 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.315 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.320 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.325 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.350 | 1.0000 | 0.9848 | 0.0000 | 1.0000 |
| 0.400 | 0.9394 | 0.9242 | 0.0000 | 0.9091 |
| 0.450 | 0.8081 | 0.8030 | 0.0000 | 0.9091 |
| 0.500 | 0.6162 | 0.6364 | 0.0000 | 0.8182 |

**Selected threshold: 0.285** (rule: negative-FP ≤ 10% → max recall@5 → max MRR)

## Reading

- The production default `SemanticSearchOptions.MinSimilarity = 0.30` sits comfortably inside the
  plateau (0.285–0.350) where recall@5 is perfect and no negative query leaks. No change needed.
- The sweep's formal winner is 0.285 only because full ties resolve to the earliest candidate;
  every threshold in the plateau is equivalent on this corpus.
- Below 0.285 negative queries start returning matches (precision collapses long before recall
  gains anything); above 0.350 recall decays steadily.
- Keyword-subset recall@5 stays 1.0 across the whole plateau — on this corpus pure semantic
  retrieval already handles acronym/product-name queries, an input to the hybrid-search decision
  (P1T-46 / P1T-54).

## Tuning verdict (P1T-53): keep `MinSimilarity = 0.30`

Applying the locked tuning protocol (P1T-45) to the sweep above: the FP ≤ 10% band is the
0.285–0.350 plateau, where recall@5, MRR, and keyword recall are metric-identical. The rule's
formal winner (0.285, earliest tie) is indistinguishable from 0.30 on every metric while sitting
at the edge of the precision cliff (0.280 already leaks negatives at 16.7%). **Decision: the
production default stays 0.30** — mid-plateau, maximum margin to both failure modes. No config
change; the regression floor in `EvalBaselines.cs` reflects the plateau numbers (recall@5 1.0).

Caveat recorded: the 24-expert frozen corpus saturates recall by design. The verdict means
"0.30 is not measurably improvable on the golden set", not "retrieval is perfect at scale"; the
live regression gate guards the floor, and the sweep is one command to rerun (below) if the
corpus or embedding model changes.

## Hybrid search verdict (P1T-54): not adopted

Applying the locked adoption rule (P1T-46) at the tuned threshold (0.30): keyword-heavy subset
recall@5 = 1.0000 vs overall recall@5 = 1.0000 — **gap 0.0 points** (rule threshold: > 10), and
no keyword query returned empty while its target existed. **Decision: hybrid keyword+vector
search (tsvector + RRF) is not implemented.** The pre-decided design remains on record in the
P1T-46 resolution should a future corpus/model change open the gap — the standing eval gate and
the keyword-recall column of every future sweep re-raise the question automatically.

## Reproducing

Needs Docker and a Gemini API key. Never commit the key.

```bash
GEMINI_API_KEY=<key> dotnet run --project tools/RetrievalEval -- \
  --sweep 0.15:0.50:0.05 --refine --date <yyyy-MM-dd> --output sweep.md
```

Single-threshold baseline check:

```bash
GEMINI_API_KEY=<key> dotnet run --project tools/RetrievalEval -- --threshold 0.30
```

The corpus and queries are embedded once (at the sweep floor); each threshold is a pure in-memory
re-rank of the cached similarities, so a full sweep costs the same embedding budget as a single run.

## Re-baseline under Gemini embeddings (2026-08-01, P1T-88)

GitHub Models retired 2026-07-30; embeddings moved to `gemini-embedding-001` (1536 dims via the
OpenAI-compatible endpoint). The 0.30 floor tuned above is **invalid for Gemini** — its similarity
scores cluster higher, so at 0.30 every negative query leaked (negative-FP 1.0).

Full re-sweep (`GEMINI_API_KEY=<key> dotnet run --project tools/RetrievalEval -- --sweep 0.30:0.80:0.05 --refine`),
same frozen 24-expert corpus and 39-query golden set:

| Threshold | Recall@5 | MRR | Negative FP rate | Keyword recall@5 |
|-----------|----------|-----|------------------|------------------|
| 0.500 | 1.0000 | 1.0000 | 0.8333 | 1.0000 |
| 0.525 | 1.0000 | 1.0000 | 0.1667 | 1.0000 |
| 0.540 | 1.0000 | 1.0000 | 0.0000 | 1.0000 |
| 0.575 | 1.0000 | 1.0000 | 0.0000 | 1.0000 |
| 0.600 | 0.9899 | 1.0000 | 0.0000 | 1.0000 |
| 0.650 | 0.7071 | 0.7273 | 0.0000 | 0.6364 |

**Verdict: `MinSimilarity` = 0.55** — mid-plateau of the perfect window 0.540–0.575, same
mid-plateau rule as the original 0.30 pick. Baseline at 0.55: recall@5 **1.0000**, MRR **1.0000**,
negative-FP **0.0000**. Floors updated in `tests/Mcp.Tests/Eval/EvalBaselines.cs`; the live gate
runs with `GEMINI_API_KEY` now.

## Azure sweep at the default (2026-09-27, EXP-57)

`text-embedding-3-small` re-measured through the real reconciler, pgvector and search service, on
the same frozen 24-expert corpus and 39-query golden set, as the evidence for making Azure the
default:

| | Azure `text-embedding-3-small` | Gemini `gemini-embedding-001` |
|---|---|---|
| Plateau (recall 1.0, 0 false positives) | 0.285–0.350 | 0.540–0.575 |
| Recall@5 / MRR on the plateau | 1.0000 / 0.9848 | 1.0000 / 1.0000 |
| Recall@5 at 0.55 | **0.3030** | 1.0000 |

**Verdict: `MinSimilarity` = 0.30 for Azure** — mid-plateau, the same rule the other two picks used.
The Azure numbers are identical row for row to the 2026-07-11 baseline at the top of this document,
which measured **the same model** through the since-retired GitHub Models endpoint: same model, same
vectors, and the provider move changes who serves it rather than what it returns.

The last row is the whole argument for a per-provider threshold. It is also why this document no
longer has "the production threshold" in the singular.

Re-measure either provider with:

```bash
dotnet run --project tools/RetrievalEval -- --provider AzureFoundry --threshold 0.30
dotnet run --project tools/RetrievalEval -- --provider Gemini --threshold 0.55
```
