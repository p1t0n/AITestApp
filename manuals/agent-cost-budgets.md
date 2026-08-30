# Agent cost budgets: the roster-qa regression and the floors that should have caught it

> **Status (2026-08-30):** step 1 of §4 has landed (P1T-144): the deterministic Cost Floors run on
> every push and the usage ledger records `Iterations` + `ToolSequence`. Values and how to
> re-measure them: `manuals/agent-eval-baselines.md` §4. Steps 2-5 are still open. Measurements below are real —
> taken from the `AgentUsages` ledger and from one live traced run of the roster-qa endpoint on
> the seeded 45-employee demo roster. Vocabulary lives in `CONTEXT.md` → *Cost & budgets*.

`roster-qa` answers "who knows react and lives in London" for **160,220 input tokens**. The
per-user daily cap is 50,000. One question is 3× a user's whole day.

This is a regression, not a design flaw. The same question on the same roster cost ~5,400 tokens
four weeks earlier.

## 1. The evidence

### 1.1 Every roster-qa call ever recorded

`GenerateDemoRoster` has been untouched since 2026-08-01, so the roster (45 employees, 150
experiences, 500 achievements, 79 catalog skills) is identical across all of these rows. Data
growth is not the cause.

| date | input | output | latency |
|---|---|---|---|
| 2026-08-02 | 5,447 | 39 | 3.8s |
| 2026-08-02 | 5,468 | 39 | 3.7s |
| 2026-08-02 | 5,500 | 47 | 3.4s |
| 2026-08-30 | 80,384 | 293 | 8.2s |
| 2026-08-30 | 146,043 | 604 | 7.5s |

**27× regression.** Input is 99.6% of the spend — this is not a verbose-answer problem.

### 1.2 Cost by agent shape

The split is not per-agent tuning. It tracks *who drives the tool calls*:

| shape | agent | calls | avg/call | worst |
|---|---|---|---|---|
| model drives the loop | roster-qa | 5 | 48,784 | 146,647 |
| model drives the loop | resume-ingestion | 1 | 157,252 | 157,252 |
| code drives the calls | roster-scan | 15 | 5,112 | 6,116 |
| code drives the calls | match | 7 | 4,062 | 4,941 |
| code drives the calls | shortlist | 8 | 1,735 | 3,596 |

An agent that decides its own tool sequence costs an order of magnitude more than one whose
sequence is written in C#. That is the cost of the flexibility, and it is worth paying — but only
when it is bounded and measured, which today it is not.

### 1.3 One traced run

Traced in-process by attaching an `ActivityListener` to the `Experimental.Microsoft.Extensions.AI`
source — the `chat` and `execute_tool` spans already ship (P1T-94, see
`manuals/maf-otel-telemetry.md`); nothing new had to be instrumented. Question: *"who knows react
and lives in London"*. Result: **160,220 in / 593 out, 11.5s, 10 chat calls, 9 tool calls.**

| # | tool called after this call | result adds | input tokens sent |
|---|---|---|---|
| 1 | `skill_list` | +7,522 | 4,202 |
| 2 | `roster_semantic_search` | +115 | 11,724 |
| 3 | `roster_semantic_search` | +73 | 11,839 |
| 4 | `roster_semantic_search` | +1,183 | 11,912 |
| 5 | `employee_list` | +4,073 | 13,095 |
| 6 | `cv_get` | +1,955 | 17,168 |
| 7 | `cv_get` | +2,921 | 19,123 |
| 8 | `cv_get` | +2,477 | 22,044 |
| 9 | `roster_shortlist_search` | +71 | 24,521 |
| 10 | — (`stop`) | — | 24,592 |

### 1.4 Where the 160,220 went

Turn Amplification makes each payload cost its size times the calls that follow it. The
decomposition closes exactly:

| what | size | re-sent | total | share |
|---|---|---|---|---|
| Baseline Prompt Size (instructions + 11 tool schemas) | 4,202 | ×10 | 42,020 | 26.2% |
| `skill_list` result (all 79 catalog skills, unfiltered) | 7,522 | ×9 | 67,698 | 42.3% |
| `employee_list` result (45 rows) | 4,073 | ×5 | 20,365 | 12.7% |
| 3× `cv_get` | ~2.5k each | ×4, ×3, ×2 | 21,537 | 13.4% |
| 3× `roster_semantic_search` | 73–1,183 | — | 8,529 | 5.3% |
| `roster_shortlist_search` | 71 | ×1 | 71 | 0.04% |
| | | | **160,220** | |

Three findings, none of them the obvious guess:

1. **`skill_list` is 42% of the bill.** The model was *right* to call it — the question says
   "react", and it wanted the skill id to filter `roster_semantic_search`. The defect is in the
   tool: `skill_list` takes no filter and no page, and dumps the whole catalog. Every other list
   tool on this surface pages. This one is the anomaly. *(Fixed in P1T-145: the same lookup is now
   two rows, an estimated 87 tokens against 3,080.)*
2. **The semantic tools are nearly free.** `roster_semantic_search` results cost 73–1,183 tokens;
   `roster_shortlist_search` costs 71. The *structured* tools are the expensive ones — the exact
   opposite of the intuition the tool descriptions are written around.
3. **26% is pure re-send of the tool surface.** Eleven schemas × ten iterations. The Description
   Bar work (P1T-128/129) is billed ten times per question.

`roster_digest_list` — the first suspect, since a full page is ~16k tokens — is **never called**.

And after 160,220 tokens the answer was *"three people in London, none of them know React."*

## 2. Root cause

Everything in the regression window was deliberate quality work:

| ticket | date | what it added | cost effect |
|---|---|---|---|
| P1T-121 | 08-16 | `roster_digest_list` | +1 schema (unused here) |
| P1T-130 | 08-16 | first-call forcing + Capture-Verify Guard | forces a tool call on turn 1; retry doubles an ungrounded run |
| P1T-128/129 | 08-28 | Description Bar passes | ~12KB of descriptions, re-sent per iteration |
| P1T-136/137 | 08-28/29 | `style_exemplar_search`, Partial Update | +schemas |

Every one shipped an accuracy floor. **None shipped a Cost Floor.** The suite stayed green
through a 27× cost regression, which is the actual failure — the individual tickets each did
what they set out to do.

## 3. Decisions

### 3.1 Two numbers, never one

A **Runtime Budget** and a **Cost Floor** are different instruments and conflating them yields
either a cap nobody can hit or a floor that flakes. The budget bounds the worst case in
production; the floor detects drift in CI.

Projection for roster-qa after the fixes below: 4 tools ≈ 1,800 Baseline Prompt Size, filtered
`skill_list` ≈ 50, one filtered `roster_semantic_search` ≈ 1,100 → **≈6,500 tokens over 3 calls**,
which is the 2026-08-02 profile restored.

| agent | Runtime Budget | max iterations | Cost Floor |
|---|---|---|---|
| roster-qa | 15,000 | 6 | 8,000 |
| resume-ingestion | 40,000 | 8 | 25,000 |
| default (unlisted) | 20,000 | 6 | — |

resume-ingestion gets more because a pasted resume is genuinely large input. That is real work,
not waste.

### 3.2 How the budget stops the loop

A token-budget `DelegatingChatClient` placed *inside* the function-invocation loop — the same seam
`MeteringChatClient` already occupies. It sums input tokens per run; once over budget it clones
`ChatOptions` with `ToolMode = None` and appends an instruction to answer from what is already in
hand. The model then writes a real closing answer, which carries a Degradation note.

`FunctionInvokingChatClient.MaximumIterationsPerRequest` is the backstop for the case a token
ceiling cannot catch: a long loop of individually tiny calls.

Both are reachable without forking: `ChatClientAgentOptions.UseProvidedChatClientAsIs` (Agents.AI
1.10.0) lets us hand `ChatClientAgent` a pipeline we built, instead of its default decorators.
Clone `ChatOptions` before mutating — the in-loop precedent is
`MessageInjectingChatClient.UpdateOptionsForNextIteration`.

Rejected: aborting the run and salvaging the last assistant text. It throws away work already
paid for and produces a worse answer than the model would write itself.

### 3.3 The floors run without a model

This is the load-bearing decision. The regression shipped green because the agent evals are live
and opt-in (`Category=eval`, needs `GEMINI_API_KEY`) — exactly the blind spot that let four
tickets through.

But look at what actually regressed: 26% tool schemas, 42% one tool's result. **Both are
measurable with zero model calls.** `skill_list` returning 7,522 tokens is a plain assertion in
`Mcp.Tests` against real Postgres, running on every push. Baseline Prompt Size is a serialization
of instructions plus the tool listing — no model needed either.

So: deterministic Cost Floors in CI, plus a live end-to-end floor on demand alongside the existing
baselines in `manuals/agent-eval-baselines.md`. The deterministic half is the one that would have
caught this.

### 3.4 Ratchet, so main stays green

Floors land at *today's measured* values with a comment naming the target, and each subsequent
change tightens them. Main is never red, every ticket carries a visible numeric delta in its diff,
and TDD survives inside each ticket: tighten the ceiling (red), implement (green).

The alternative — landing target values that stay red until the last ticket — cannot be merged
sequentially, and stacked PRs are off the table.

## 4. The work

Sequential, each landing on its own:

1. **Cost made measurable** (P1T-144). Deterministic Cost Floors for Baseline Prompt Size and per-read-tool
   result size (`Mcp.Tests`, `Agents.Tests`), landed as Ratchets at today's values.
   `Iterations` + `ToolSequence` columns on `AgentUsage`, so the ledger says *why* a call was
   expensive without anyone writing a throwaway probe.
2. **`skill_list` filter + paging** (P1T-145) — *shipped*. `nameContains` (case-insensitive
   substring) plus `page`/`pageSize`, mirroring the rest of the list surface. Result ceiling
   ratcheted 3,080 → **87** for the lookup the traced run wanted (`nameContains: "React"` matches
   React and React Native), with the unfiltered sweep ratcheted separately at 3,100 so it stays
   measured. The schema ceiling was **raised** 176 → 308, the one deliberate re-baseline in this
   chain: see `manuals/agent-eval-baselines.md` for the trade. The Tool-Selection Eval floors were
   **not** re-baselined — that burns real free-tier quota and the golden set is frozen, so it rides
   along with P1T-148, which re-runs the eval twice anyway.
3. **Tool Allowlist** (P1T-146). Per-agent read-tool subset in `McpToolSource`; roster-qa gets 4 of 11.
   Ratchet the Baseline Prompt Size ceiling down.
4. **Budget seam** (P1T-147). §3.2, applied to every agent. resume-ingestion is covered for free.
5. **Convergence** (P1T-148). Instructions and descriptions so roster-qa answers in ~3 calls rather than 10.
   Cost Floor to 8,000. Last, because it needs the Tool-Selection Eval re-run twice.

### Two things worth saying out loud

- **P1T-144–147 bound the cost; P1T-148 is what restores 5–8k.** The duplicate semantic searches
  and speculative `cv_get`s are a Convergence problem. If P1T-148 is dropped, the honest target
  is "15,000, capped" — not 6,500.
- **The Tool-Selection Eval re-baseline burns free-tier quota** — the same budget this work
  exists to protect. P1T-145 therefore did not pay it: its floors were left untouched (never
  loosened) and the frozen golden set left frozen, so the re-run folds into P1T-148, which pays
  it twice regardless. The prompt worth adding at that point is a skill-id *lookup* ("what is the
  catalog id for React?"), which the pre-P1T-145 tool had no cheap answer to.

### Backlog, from measured evidence

- **Server-side per-tool MCP scopes** (P1T-149). The Tool Allowlist belongs on the agent's Keycloak identity
  (`agent-roster-qa`), enforced server-side, rather than in client config. P1T-146 is the
  client-side stand-in.
- **resume-ingestion's tool choice** (P1T-150). 157,252 tokens in a single recorded call, same loop shape,
  and it holds `mcp:read mcp:write`. Its own investigation.

## 5. Reproducing the measurement

The ledger now answers most of this on its own: every `AgentUsages` row carries `Iterations` and
`ToolSequence` alongside the token counts, so "why was this call expensive" is a query, not a
probe. The deterministic Cost Floors re-measure the payload sizes on every push
(`manuals/agent-eval-baselines.md` §4).

For a full per-call token breakdown of a live run, attach an
`ActivityListener` to `Experimental.Microsoft.Extensions.AI` around a
`WebApplicationFactory<Program>` call to `POST /agents/roster-qa`, and read
`gen_ai.usage.input_tokens` off each `chat` span plus the tool name off each `execute_tool` span.
The `orchestrate_tools` span carries the run total.

Note that the Aspire dashboard (`docker-compose.yml`, port 18888) receives these spans but holds
them in memory only — the 2026-08-30 traces were already gone by the time this was investigated,
which is the argument for P1T-144's ledger columns.
