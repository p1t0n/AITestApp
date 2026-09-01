# Roster Scan — async bulk scoring and the sync-vs-batch selection

One job description in, every (optionally filtered) active expert scored against it — as a
durable, pausable background job rather than a blocking call. Roster Scan is the knowledge-item-3
artifact of map P1T-105: choosing between the synchronous Messages-style API and an asynchronous
batch API based on latency requirements, workflow blocking, and acceptable processing windows —
and building the choice as a swappable seam instead of a hard-coded answer.

> Decision trail: research **P1T-107** ([`gemini-batch-api.md`](gemini-batch-api.md)), grilling
> **P1T-110**, quota facts **P1T-114**. Build slices: **P1T-121** (`roster_digest_list` MCP tool)
> → **P1T-122** (ScoringJob domain) → **P1T-123** (transport seam) → **P1T-124** (runner) →
> **P1T-125** (endpoints + widget tab) → **P1T-126** (this doc + live smoke).

## The selection, recorded

| Criterion | Reading for bulk roster scoring |
| --- | --- |
| Latency / blocking | Nobody waits on ~50 scores at once — **async job semantics regardless of transport**. |
| Acceptable window | Hours are fine. Anthropic Batches targets ~1h (24h hard); Gemini Batch targets ~24h; the free-tier fallback is bounded by RPD windows. |
| Volume economics | Both providers price batch at 50% of standard — irrelevant on the free tier. |
| Free-tier reality | Gemini Batch is **"Not available"** on the free tier (pricing page; batch quotas exist only for Tiers 1–3). |
| `IChatClient` | Has **no batch abstraction** — a real batch integration bypasses the OpenAI-compat + metering/OTel stack entirely. |

**Decision**: async job semantics with a transport seam. The default transport is self-hosted
queued sync (client-side batch: persisted job + channel worker + rate pacing); a real Gemini
Batch transport is a config flip away, not a rewrite.

## The seam: `IScoringTransport`

`api/Agents/RosterScan/ScoringTransport.cs` — score one chunk of candidate digests against the
JD + its `JdRequirements` extraction; the runner never knows the transport.

- **Default (`QueuedSyncScoringTransport`, free tier)**: one tool-less, schema-constrained chat
  call per chunk (~10 digests, `RosterScan:ChunkSize`), paced by a process-wide
  `FixedWindowRateLimiter` (`RosterScan:RequestsPerMinute`, default 12 — headroom under the
  pinned model's 15), bounded exponential-backoff 429 retries, then a typed
  `ScoringQuotaExceededException`. Checked, never trusted: every chunk member gets exactly one
  result row — unknown ids dropped, missing members failed honestly, out-of-range scores nulled,
  `scorable: false` + nulls is the legal "nothing to judge" outcome.
- **Future (Tier 1)**: a `GeminiBatchScoringTransport` over `Google.GenAI client.Batches`
  (async submit, JSONL results, ~24h target window, 50% price) slots in behind the same
  interface. It would own its own metering seam (batch bypasses `IChatClient`). Deliberately
  **not built** — the free tier can't exercise it; the seam + this note are the record.

## Quota arithmetic (P1T-114, pinned `gemini-3.5-flash-lite`)

RPM 15 / TPM 250K / RPD 500 (free tier, per project, resets midnight Pacific). A 500-expert
scan = 50 chunk calls + 1 extraction ≈ **4 minutes** under RPM pacing and a tenth of the day's
RPD; the 45-expert dev roster completes in seconds. Every Flash-proper generation is an RPD-20
trap — never trust a `-latest` alias for quota.

## Job model

`ScoringJob` (jsonb extraction + filters) with `ScoringJobCandidate` rows that capture their
digest at intake — a resumed job scores exactly what the original sweep saw.

- Job states: `queued → running → paused | completed | failed`; `paused → queued/running`;
  terminal states never resurrect (guarded transition map, race-safe).
- Candidate states: `pending → scored | failed`; failed counts as settled — progress always ends
  at total/total (the staffing N/N rule).
- **Pause/resume is the normal path**: the transport's quota exception →
  `paused(quota, resumeAt = next midnight Pacific)`; a tripped user cap →
  `paused(cap, resumeAt = the window's reset)`, checked before every chunk so a capped job never
  spends. A timer sweep re-queues due jobs; startup recovery re-queues orphaned `running` rows.
- Intake is idempotent: one metered JD extraction (`jd-extraction`, persisted), one digest sweep
  via `roster_digest_list` through the `agent-roster-scan` identity (mcp:read), filtered by the
  deterministic eligible-id set (`IExpertFilterService` — semantic search's prefilter
  semantics). Chunk replies meter under `roster-scan`.

## Surfaces

- `POST /agents/roster-scan` → 202 `{jobId, estimate: {candidates, calls, rpdBudget}}`.
  **No cap 429 by design** — a scan is a job; with the cap tripped it accepts and parks
  `paused(cap)` immediately.
- `GET /agents/roster-scan/{id}` → polling contract (no SSE — jobs span hours and pauses):
  state, pause metadata, progress, results so far (scored by score desc; null score omitted).
  Requester-scoped; someone else's job is a 404. `GET /agents/roster-scan` → light index.
- **Widget "Scan" tab**: estimate line, 3s polling progress bar, paused banner with resume time
  (partial results stay visible), honest rows (band·score chip / "Not scorable" / error), and
  **Open in Match** drill-in for deep per-candidate assessment. The job keeps running when the
  panel closes.

## Testing

- `QueuedSyncScoringTransportTests` — request shape, hygiene ladder, pacing, 429/quota ladder.
- `ScoringJobStoreTests` — transition guards, batch writes, resume sweep, ordering.
- `RosterScanRunnerTests` — intake idempotence, chunking, quota pause to the exact Pacific
  reset, cap pause before spending, resume without re-scoring, honest terminals, metering rows.
- `RosterScanEndpointTests` — 202 + estimate, accepts-even-when-capped, polling shape, scoping.
- `AgentWidget.rosterscan.test.tsx` — estimate, progress, paused banner, not-scorable, drill-in.
- `RosterScanLiveSmokeTests` (`Category=live`) — a real scan end to end over the seeded roster,
  ranked honest results; pauses honestly (skip, not fail) if the day's quota is genuinely gone.
