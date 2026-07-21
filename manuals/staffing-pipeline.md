# Staffing Pipeline — multi-agent orchestration on MAF

One request that automates the manual loop recruiters were running by hand across the widget
tabs: paste a JD into Shortlist, drill each promising candidate into Match, compare the answers,
pick someone. `POST /agents/staffing` runs that whole loop as a workflow — Shortlist → Match
fan-out across the top candidates → a narrative comparison — and streams progress live, ending in
one composite, evidence-linked, **recommendation-first** report. Deterministic facts (ids, scores,
coverage, evidence) come from captured tool results and step outputs only; model prose is confined
to rationales and the validated recommendation, per the standing agents-service rules.

> Decision trail: wayfinder map **P1T-69** — research **P1T-70** (MAF primitives, see
> [`maf-orchestration-primitives.md`](maf-orchestration-primitives.md)), product contract
> **P1T-71** (inputs/report/SSE/cost), architecture **P1T-72** (workflow shape, identity, fan-out,
> failure policy), UI **P1T-73** (Staffing tab, progress, report rendering).
> Build slices: **P1T-74** (run-service extraction) → **P1T-75** (pipeline) → **P1T-76** (SSE
> endpoint) → **P1T-77** (Staffing tab) → **P1T-78** (this doc).
> The shortlist and match steps it composes are documented in
> [`semantic-roster-search.md`](semantic-roster-search.md).

---

## Architecture

The pipeline is a Microsoft Agent Framework workflow (`Microsoft.Agents.AI.Workflows` 1.10.0,
exact-pin aligned with the rest of the agent stack) over **extracted run services**
(`api/Agents/Staffing/StaffingPipeline.cs`):

```
POST /agents/staffing (Program.cs — thin shell: 400/401/429 pre-checks, then SSE)
        │
        ▼
StaffingPipeline.RunAsync            one WorkflowBuilder chain per run (per-run executors)
        │
  prepare ──► shortlist ──► match ──► aggregate ──► narrative ──► report
  (clamp       │             │          (evidence     (tool-less     (compose +
   matchTop)   │             │           markdown)     JSON call)     YieldOutputAsync)
               │             │
               │             ├─ Task.WhenAll over ≤ matchTop candidates
               │             │  behind StaffingThrottle (SemaphoreSlim, default 2)
               │             │  + 429-aware StaffingRetryPolicy per candidate
               │             │
        IShortlistRunService IMatchRunService        IChatClient ("staffing")
        (ShortlistAgent →    (MatchAgent →           narrative completion,
         roster_shortlist_    per-candidate gap       no tools, no MCP
         search via MCP)      analysis via MCP)
```

### Run-service extraction (P1T-74)

The pipeline consumes the *same cores* the one-shot endpoints use, extracted from the endpoint
bodies into `ShortlistRunService` / `MatchRunService` (`api/Agents/Agents/`):

- **`IShortlistRunService.RunAsync(ShortlistAgentRequest)`** → `ShortlistRunOutcome`: runs the
  `ShortlistAgent` and composes the full response via `ShortlistComposer` (templated-rationale
  degrade and the corruption guard included). Exactly one of `Response`/`FaultDetail` is non-null;
  `Reply` (token usage) is always present because tokens were spent either way — the caller meters
  before deciding what to do with a fault.
- **`IMatchRunService.RunAsync(employeeId, jobDescription)`** → `MatchRunOutcome`: owns the match
  prompt template (single source of truth) and runs the `MatchAgent`.

The run services carry **no HTTP types, no cap-checks, no metering** — those stay with the caller,
which orchestrates them differently per surface: the one-shot endpoints (`POST /agents/shortlist`,
`POST /agents/match`) remain thin shells doing validation → cap pre-check → run → meter → HTTP
fault mapping (502 on upstream faults), while the pipeline weaves the same steps into its workflow
with its own cap and failure semantics. The interfaces exist so the pipeline's tests substitute
fakes (`tests/Agents.Tests/Fakes/FakeStaffingSteps.cs`); the real services need a live agent stack.

### Workflow spine + bounded internal fan-out (P1T-75)

The graph is an explicit `WorkflowBuilder` chain of typed `FunctionExecutor`s exchanging private
stage DTOs (`PreparedStage → ShortlistStage → MatchStage → EvidenceStage → NarrativeStage →
ReportResult`) — **not** the chat-message high-level builders, whose protocol would force our typed
outcomes through chat messages. Executors are per-run instances closing over a per-run `Runner`,
so every stage is strictly request-scoped; the run executes via `InProcessExecution.RunAsync` and
the sink yields the result with `context.YieldOutputAsync` (declared through the executor's
`outputTypes`).

The match fan-out deliberately runs as a bounded-parallel task group **inside** the match executor
rather than as graph-level `AddFanOutEdge`/`AddFanInBarrierEdge`, for two reasons (documented on
the class):

1. **N is dynamic.** The barrier fan-in expects a message from every build-time target, so a
   shortlist returning fewer than `matchTop` candidates would need sentinel work items purely to
   unblock the barrier.
2. **The real concurrency bound is the shared throttle** (default 2), which makes graph-level
   fan-out purely cosmetic — a `Task.WhenAll` behind the same semaphore gives identical,
   deterministically testable scheduling.

MAF 1.10 has no fan-out max-concurrency knob (the research finding that shaped this), so the
throttle is ours: see below.

### Service registration (Program.cs)

- `StaffingThrottle` — **singleton** (process-wide): the throttle protects the model endpoint's
  rate limit across *all* concurrent staffing requests, not per run.
- `StaffingRetryPolicy.Default` — singleton.
- `StaffingPipeline` — **scoped**: it meters/cap-checks through the request-scoped usage services.
  Its narrative chat client resolves via `ResolveAgentChatClient("staffing")` — the shared default
  model unless a `GitHubModels:Agents:staffing` override is configured.

---

## Semantics

### Inputs

`POST /agents/staffing` body (camelCase; same filters as shortlist):

```json
{
  "jobDescription": "…",          // required; blank → 400 before the stream opens
  "availableOn": "2026-08-01",    // optional filters, passed through to the shortlist step
  "skillIds": ["…"],
  "location": "…",
  "minYears": 3,
  "matchTop": 3                   // optional; default 3, clamped to 1..5
}
```

`matchTop` (default **3**, hard cap **5**) is both the shortlist's `topK` and the match fan-out
width: the shortlist step retrieves exactly the candidates the fan-out will assess.

### Throttle + 429 retry

- **`Staffing:MaxConcurrentMatches`** (default **2**): a process-wide `SemaphoreSlim` sized from
  config. A slot is held for a candidate's *whole* match attempt — retries included — so backoff
  never over-admits new model calls. A candidate's "started" progress event fires only once a slot
  is held: it marks real work, not a queued task.
- **`StaffingRetryPolicy.Default`**: up to 3 attempts per candidate, linear backoff (5 s, then
  10 s), retrying **only** 429-shaped faults (`HttpRequestException`/`ClientResultException` with
  status 429 — GitHub Models' short per-minute limits). Any other failure is a real answer and
  fails the candidate immediately.

### Caps (degrade, not 500)

Per-user token caps are enforced at three points, and a mid-run trip **degrades the report instead
of failing the request**:

1. **Endpoint pre-check** — a tripped cap answers plain HTTP 429 (structured body: window, used,
   cap, resetAt) before the stream opens.
2. **Re-check before the match fan-out** — the shortlist step just spent tokens. On a trip, no
   match runs launch: every candidate ships `match.status: "skipped"` and the report carries one
   cap note covering matches and narrative.
3. **Re-check before the narrative call** — the fan-out just spent tokens. On a trip the narrative
   is skipped (note + `degraded: true`); rationales fall back to templates.

Defaults (`Usage` section): **daily 50 000** (raised from 25k in P1T-75 — one staffing run is
roughly `matchTop + 2` model conversations), weekly 150 000, monthly 500 000. An unidentified
principal (no user id claim) runs unmetered and uncapped, mirroring the one-shot endpoints.

### Failure ladder

Everything downstream of a successful shortlist **degrades into the report and never throws**:

| Failure | Outcome |
| --- | --- |
| Shortlist step fails (MCP/auth/model fault, or the model skipped the tool) | The pipeline's **one** error outcome (without a shortlist there is nothing to report), surfaced as data: `StaffingRunOutcome.ShortlistFault` → SSE terminal `error` event (the one-shot endpoints map the same class of fault to 502) |
| One match run fails (after retries) | `match.status: "failed"` + per-candidate error + a report note; other candidates unaffected; `degraded: true` |
| Cap trips mid-run | `skipped` statuses + cap note (see above); `degraded: true` |
| Narrative call fails or its JSON is unparseable | Templated rationales + note; no recommendation; `degraded: true` |
| Narrative recommendation missing / names an unknown candidate | Recommendation dropped (note, `degraded: true`); the model's valid rationales are kept |
| Anything escaping the ladder | A pipeline bug by definition — surfaced as an exception (SSE maps it to the terminal `error` event) |

### Narrative step (tool-less, validated)

The narrative is **not an agent** — it is a plain tool-less chat completion owned by the pipeline
(no agent identity, no MCP access): all its facts arrive pre-assembled in the prompt by the
aggregate step (shortlist coverage + evidence, plus each candidate's match markdown, truncated to
1 500 chars). It must reply with minimal JSON (`rationales[]` + `recommendation`), parsed
leniently (direct parse, then a retry on the outermost `{…}` span). Corruption guards keep model
output honest:

- rationales for **unknown employee ids are dropped** — the deterministic template covers those
  candidates (`Matched m/t shortlist requirements; match score s/100 (band).`);
- the recommendation **must name one of the report's candidates** or it degrades to none
  (note + `degraded: true`) — the report never carries an invented pick.

### Parsed match score/band

`MatchAnswerParser` (pure, `api/Agents/Staffing/MatchAnswerParser.cs`) lifts the deterministic
facts out of each Match answer: the overall score (0–100) and the band (Strong / Moderate / Weak /
Insufficient evidence). It scans only lines mentioning "overall"/"band" (never gap-analysis prose)
and returns nulls — never throws — when nothing readable is found; the raw markdown ships in the
report regardless. The parsed facts make the report sortable/renderable (the UI's band·score chip).

### Metering

Each step meters its own token usage under its agent name, so the Usage tab's per-agent breakdown
stays truthful:

| Row | What it counts |
| --- | --- |
| `shortlist` | The pipeline's shortlist step (same name as the one-shot endpoint) |
| `match` | One row per candidate match run |
| `staffing` | The narrative completion (the only usage owned by the pipeline itself) |

Match replies are metered sequentially *after* the fan-out — the EF-backed meter is a scoped
service and not safe for concurrent use. Faulted shortlist runs are metered before the fault is
handled: tokens were spent either way.

---

## SSE contract (P1T-76)

`POST /agents/staffing` responds `text/event-stream` **only** — the pre-checks (401 auth, 400
blank job description, 429 cap) answer as plain HTTP *before* the stream opens. The contract is
pinned in `api/Agents/Staffing/StaffingSse.cs`; payloads are camelCase, optional fields omitted
when absent.

In run order:

- **`event: step`** — `{ "stage": "shortlist|match|narrative", "status": "started|completed",
  "candidate"?: { "employeeId", "name" }, "completedCount"?, "totalCount"? }`. Enough for a
  stepper UI: shortlist started/completed, match started/completed per candidate (name + k/N
  counters — `completedCount` counts every *finished* match run, failed ones included, so progress
  always ends at N/N), narrative started/completed.
- **`event: stepFailed`** — the same shape with `"status": "failed"` plus an `"error"` message;
  the run continues under the degrade policy (the report ships `degraded: true`). Stages a cap
  trip skips emit no step events at all — the report's `skipped` statuses and cap note are the
  signal.
- **`event: report`** — terminal; data is the full pinned staffing report (below), serialized
  exactly like the rest of the agents API.
- **`event: error`** — terminal; problem-style `{ "title", "detail" }` for the unrecoverable
  outcomes: a failed shortlist (nothing to report) or an unexpected fault. A failed shortlist
  intentionally emits **no** `stepFailed` — that event promises the run continues, and this one
  cannot.

While no event is ready the stream carries periodic `: ka` comment lines as keep-alives
(`Staffing:SseKeepAliveSeconds`, default 15), so proxies and idle-timeout middleboxes keep the
stream open. Exactly one terminal event closes the stream. **Client disconnect cancels the
in-flight pipeline run** through the request-aborted token.

Internally the pipeline's ordered `IProgress<StaffingProgressEvent>` events are bridged through an
unbounded channel to the response writer, one flushed frame per event; message-only diagnostics
(prepare/aggregate/report chatter, cap-trip skips) never reach the wire.

Demo with curl (`-N` disables buffering):

```bash
curl -N http://localhost:5200/agents/staffing \
  -H 'Content-Type: application/json' \
  -d '{"jobDescription":"Senior backend engineer: event streaming, cloud infrastructure.","matchTop":2}'
```

---

## Report contract (P1T-71)

The terminal `report` event's data (camelCase; shapes pinned in
`api/Agents/Staffing/StaffingReport.cs`):

```jsonc
{
  "requirements": ["event streaming", "cloud infrastructure"],  // how the JD was read (shortlist step)
  "candidates": [                                               // ranked as the shortlist returned them
    {
      "employeeId": "…",
      "name": "…",
      "title": "…",
      "shortlist": {                       // deterministic, from the captured tool result
        "score": 0.83,
        "coverage": { "matched": 2, "total": 2 },
        "requirements": [ { "text": "…", "matched": true, "snippet": "…" } ]
      },
      "match": {                           // that candidate's match run
        "status": "completed",             // completed | failed | skipped
        "score": 91,                       // parsed from the answer; null when unreadable
        "band": "Strong",                  // ditto
        "answer": "…full match markdown…", // ships regardless of parseability
        "error": null                      // set only on failure
      },
      "rationale": "…"                     // narrative's, or the deterministic template on degrade
    }
  ],
  "recommendation": {                      // null when the narrative degraded — never invented
    "employeeId": "…",
    "narrative": "…"
  },
  "degraded": false,                       // true on any partial result
  "notes": []                              // human-readable explanations of every degrade
}
```

---

## UI — the Staffing tab (P1T-77)

The agent widget gained a **Staffing** tab (`web/src/components/AgentWidget.tsx`), streaming over
a minimal fetch-based SSE-over-POST client (`web/src/sse.ts` — the axios clients buffer whole
responses, so streaming goes through `fetch` + `ReadableStream`; pre-stream HTTP failures like the
429 cap body surface as `SseHttpError`).

- **Inputs**: JD textarea with preset chips, collapsible optional filters (available-on date,
  catalog-backed skills autocomplete, location, min years), and a "Candidates to match" selector
  (1–5, default 3 — always sent since the selector always shows a concrete value; the server owns
  every other default).
- **Live stepper**: Shortlisting → Matching (k/N) → Composing recommendation → Done, folded from
  the `step`/`stepFailed` frames. Each finished match run adds a per-candidate tick (with a
  warning icon when that run failed — a failed match warns inline but never fails the stage, and
  cap-skipped stages simply stay pending until the report settles everything).
- **Recommendation-first report**: the recommendation block renders first (highlighted border,
  linked employee, narrative; an explicit "no recommendation for this run" body when it degraded),
  then the requirement chips ("how the JD was read"), then ranked candidate cards.
- **Candidate cards**: shortlist similarity + coverage chips, the match verdict chip
  (`Band · score`) or an error/skipped chip, the rationale, collapsible per-requirement **Evidence**
  (verdict icons + snippets) and collapsible **Match details** (the full match markdown), and two
  drill-ins — **Open in Match** and **Tailor CV** — that jump to those tabs pre-filled with the
  employee and the *submitted* JD (not the live field).
- **Degrade rendering**: `degraded: true` shows an amber "Partial results" banner listing the
  report's notes; terminal `error` frames and pre-stream failures render as the standard error
  panel. Switching tabs, closing the widget, or resubmitting aborts the in-flight stream (which
  cancels the server-side run).

---

## Live-verified status

The end-of-chain verification (P1T-77) exercised the shipped slice against the real stack —
Postgres + Keycloak + MCP + Agents + SPA, real GitHub Models calls:

- Full pipeline run through the real UI (Staffing tab): stepper advanced through
  shortlist → per-candidate match ticks → narrative, ending in a recommendation-first report.
- SSE streams **incrementally** through the Vite dev proxy — verified with a timestamped probe
  (frames arrive as steps complete, not buffered into one flush).
- Per-step metering rows observed in the Usage tab: `match` / `shortlist` / `staffing`.
- Drill-in prefill works: Open in Match / Tailor CV land on those tabs with the employee and the
  submitted JD filled in.

---

## Testing

- `tests/Agents.Tests/StaffingPipelineTests.cs` — pipeline semantics over fake run services
  (`Fakes/FakeStaffingSteps.cs`): clamping, throttle, retries, cap re-checks, failure ladder,
  narrative guards, progress-event ordering.
- `tests/Agents.Tests/StaffingEndpointTests.cs` (+ `SseTestClient`) — the SSE contract end to end
  against the in-process host: pre-checks, frame order, terminal events, disconnect cancellation.
- `tests/Agents.Tests/MatchAnswerParserTests.cs` — score/band extraction.
- `tests/Agents.Tests/StaffingLiveSmokeTests.cs` — opt-in (`Category=live`) real-model smoke.
- `web/src/components/AgentWidget.staffing.test.tsx` + `web/src/sse.test.ts` — stepper reduction,
  report rendering, degrade banner, drill-ins; SSE parser edge cases.
