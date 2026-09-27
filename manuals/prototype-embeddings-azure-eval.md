# PROTOTYPE (EXP-57): retrieval eval on Azure OpenAI text-embedding-3-small

Throwaway branch. Produced by `tools/RetrievalEval` with the two-line config change in `Program.cs` (Azure v1 endpoint + deployment name; Azure key passed via `GEMINI_API_KEY` for the process only). Deployment: `text-embedding-3-small`, `DataZoneStandard`, capacity 120, in `experttojob-openai-swc`.

Command: `GEMINI_API_KEY=<azure key1> dotnet run --project tools/RetrievalEval -- --sweep 0.15:0.80:0.05 --refine --date 2026-09-27`

## Retrieval eval sweep

- Embedding model: `text-embedding-3-small`
- Corpus size: 24 experts
- Golden set: 39 queries
- Date: 2026-09-27

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
| 0.550 | 0.3030 | 0.3030 | 0.0000 | 0.3636 |
| 0.600 | 0.2727 | 0.2727 | 0.0000 | 0.3636 |
| 0.650 | 0.0909 | 0.0909 | 0.0000 | 0.0909 |
| 0.700 | 0.0303 | 0.0303 | 0.0000 | 0.0000 |
| 0.750 | 0.0000 | 0.0000 | 0.0000 | 0.0000 |
| 0.800 | 0.0000 | 0.0000 | 0.0000 | 0.0000 |

**Selected threshold: 0.285** (rule: negative-FP ≤ 10% → max recall@5 → max MRR)

## Rate-limit probe

200 concurrent 1-input requests (50 parallel): 200 × HTTP 200, no 429. Responses carry only `x-ratelimit-limit-tokens: 120000` / `x-ratelimit-remaining-tokens` (a token-per-minute limit); no request-count header. A 429's `Retry-After` shape was therefore not observed.
