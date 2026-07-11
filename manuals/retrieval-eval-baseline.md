# Retrieval eval baseline (P1T-52)

Measured retrieval quality of the semantic roster search against the frozen eval corpus
(`tools/RetrievalEval.Core/Fixtures/eval-corpus.json`) and golden set
(`tools/RetrievalEval.Core/Fixtures/golden-set.json`), embedded with the real production
pipeline (`SearchIndexReconciler` → pgvector → `SemanticSearchService`).

The committed regression floor lives in `tests/Mcp.Tests/Eval/EvalBaselines.cs` and is asserted
by `RetrievalEvalLiveTests` (`dotnet test --filter "Category=live"` with `GITHUB_TOKEN` set).

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
- Corpus size: 24 employees
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
  (P1T-45).

## Reproducing

Needs Docker and a GitHub Models PAT. Never commit the token.

```bash
GITHUB_TOKEN=<pat> dotnet run --project tools/RetrievalEval -- \
  --sweep 0.15:0.50:0.05 --refine --date <yyyy-MM-dd> --output sweep.md
```

Single-threshold baseline check:

```bash
GITHUB_TOKEN=<pat> dotnet run --project tools/RetrievalEval -- --threshold 0.30
```

The corpus and queries are embedded once (at the sweep floor); each threshold is a pure in-memory
re-rank of the cached similarities, so a full sweep costs the same embedding budget as a single run.
