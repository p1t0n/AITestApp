# Agent cost budgets: the roster-qa regression and the floors that should have caught it

> **Status (2026-08-30):** all five steps of §4 have landed. P1T-144: the deterministic Cost
> Floors run on every push and the usage ledger records `Iterations` + `ToolSequence`. P1T-145:
> `skill_list` filters and pages. P1T-146: every agent identity carries a Tool Allowlist under
> `McpAuth:<agent>:Tools`, applied in `McpToolSource` before any agent sees a tool — roster-qa is
> shown 4 of the 11 read tools. P1T-147: every agent run is bounded by a Runtime Budget (§3.2).
> P1T-148: roster-qa's instructions and `roster_semantic_search`'s description point at the
> Convergent Path, and a Convergence floor prices the whole reference run model-free (§6) —
> **6,993**, inside the 8,000 target. The Tool-Selection Eval re-baseline is still owed. Values
> and how to re-measure them: `manuals/agent-eval-baselines.md` §4. P1T-150: resume-ingestion's
> 157,252-token call is decomposed and its iteration ceiling fixed (§7). Measurements below are real —
> taken from the `AgentUsages` ledger and from one live traced run of the roster-qa endpoint on
> the seeded 45-employee demo roster; §6's are measured model-free in CI. Vocabulary lives in
> `CONTEXT.md` → *Cost & budgets*.

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
   Bar work (P1T-128/129) is billed ten times per question. *Fixed by P1T-146*: roster-qa is now
   shown the four tools this run actually called, so the same ten iterations re-send 1,744 tokens
   rather than 4,202 — 17,440 instead of 42,020.

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
| resume-ingestion | 40,000 | 24 | §6.1 |
| default (unlisted) | 20,000 | 6 | — |

resume-ingestion gets more because a pasted resume is genuinely large input. That is real work,
not waste — *and it turned out to be 3.5% of a measured run, so it is not why this agent is
expensive; see §6.* Its iteration ceiling was 8 until P1T-150 measured a faithful ingestion at 17
model calls and found the ceiling sitting under the work itself (§6.3).

### 3.2 How the budget stops the loop

**Shipped (P1T-147)** as `RuntimeBudgetChatClient`, a `DelegatingChatClient` placed *inside* the
function-invocation loop — the same seam `MeteringChatClient` already occupies, so it observes
every iteration rather than only a run's first and last call. Before each model call it reads what
the run has already spent; once over budget it clones `ChatOptions` with `ToolMode = None` and
appends a Closing Turn instructing the model to answer from what is already in hand. The model
then writes a real closing answer, which carries a Degradation note.

Rejected: aborting the run and salvaging the last assistant text. It throws away work already
paid for and produces a worse answer than the model would write itself.

**The run boundary is the `MeteringScope`** every agent already opens around a run (P1T-95). That
scope is exactly the unit a per-run ceiling must be measured over, and it is ambient, so one
budget wrapper serves concurrent runs without leaking spend between them. It also means the
Capture-Verify retry (P1T-130) spends the *same* budget as the first attempt rather than a fresh
one — which is the wanted behaviour: a run that burned its ceiling without grounding anything
should not be given a second ceiling to burn.

**The budget hangs off `ResolveAgentChatClient`**, the one place every agent asks for a model. An
agent cannot opt out of its ceiling, a new agent inherits the default without anyone remembering
to wire it, and resume-ingestion is covered with no agent-specific code. The wrapper is per-agent
(budgets differ); the model client under it stays shared.

Two things the seam deliberately does *not* do:

- It stands down when there is nothing to withdraw — no tools on offer, or tools already off. The
  code-driven agents (match, shortlist, roster-scan) send no tools at all, so a Closing Turn there
  would be a pointless extra instruction on a call that was never going to loop.
- It appends the prose note only when the run did not ask for a JSON schema. Appending prose to a
  schema-constrained response would break the caller's parse, so resume-ingestion and match read
  the same Degradation off `AgentReply.Degradation` instead.

#### Why both ceilings live in the seam

The iteration ceiling was planned as `FunctionInvokingChatClient.MaximumIterationsPerRequest` and
is instead a second check in the same client. Two reasons, found while wiring it:

1. Reaching MAF's limit stops the loop mid-flight and can hand back an *unanswered* tool call —
   the truncation this ticket exists to avoid. The seam's ceiling degrades the same way the token
   ceiling does: tools withdrawn, real closing answer.
2. Setting it at all requires `ChatClientAgentOptions.UseProvidedChatClientAsIs`, which drops
   *every* default decorator, not just function invocation. In Agents.AI 1.10.0 that stack also
   holds `AIContextProviderChatClient`, `MessageInjectingChatClient` and
   `NonApprovalRequiredFunctionBypassingChatClient` — all internal types we cannot reconstruct.
   Trading a working approval bypass for a hard iteration stop, across six agents, is a bad deal.

`ChatClientAgentRunOptions.ChatClientFactory` is not an alternative: it decorates the agent's
already-built pipeline, so it lands *outside* the loop and sees one call per run.

Clone `ChatOptions` before mutating — the loop reuses the caller's instance, so flipping it in
place would silently disarm roster-qa's first-call tool forcing well past the current call.

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

### 3.5 The Tool Allowlist sits on the identity, not in the agent

Most agents already narrowed their own surface with a `.Where(t => t.Name == ...)` at the point
they hand tools to the model. That is not the same instrument and it does not do this job:

- It runs *after* the whole surface has been fetched, so it bounds nothing the agent forgets.
  roster-qa filtered nothing and paid for eleven schemas, ten times over. Nothing in the codebase
  said that was wrong.
- It is a per-call decision, not a per-identity one. "Which tools may this agent see" is a
  capability question, and capability is enforced by the token — the same reason `mcp:read` gates
  write tools rather than a prompt saying "don't write".

So the allowlist is configured on `McpAuth:<agent>` next to the client id and scope, and applied
once in `McpToolSource` where the tool list arrives. The in-agent filters stay: the allowlist is
the outer bound on the identity, the filter is which of those tools *this turn* offers — CV
Tailoring genuinely shows `cv_get` on turn one and `style_exemplar_search` on turn two.

Two guards, because a narrowing feature's failure mode is silent:

- **An absent list means "everything the token carries."** A forgotten key must not quietly cripple
  an agent. A test asserts every registered identity nevertheless has one.
- **An allowlisted tool the server never advertised is logged as a warning.** A typo, or a scope
  that no longer carries the tool; either way the agent runs narrower than configured and says so.
  Not a failure — the tools it can still reach are an honest surface, and this is a degrade, not
  a 500.

`CostFloors.AgentToolAllowlists` is the declaration the Baseline Prompt Size floor measures
against, and a test asserts the shipped `appsettings.json` matches it. Without that link the
committed cost ceilings would stop describing the running system the first time someone edited
config.

**Since P1T-149 the identity carries it for real** (`manuals/mcp-tool-grants.md`): the same set is
`mcp:tool:<name>` scopes on the agent's Keycloak client, and the MCP server — not the client —
narrows `tools/list` and refuses `tools/call` outside them. The config key above stays as the
local echo, still asserted against the same declaration, and a second test asserts the realm
against it too. So this section's cost argument is unchanged and its "wrong place" argument is
now answered in the right place.

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
3. ~~**Tool Allowlist**~~ (P1T-146) — **landed**. Per-agent tool subset applied in `McpToolSource`,
   configured as `McpAuth:<agent>:Tools` and declared in `CostFloors.AgentToolAllowlists` (the
   floor measures against that declaration, and a test asserts the shipped config matches it).
   roster-qa: 4 of 11, Baseline Prompt Size 4,409 → 1,876. Every other identity got an explicit
   list too — an absent list still means "everything the token carries", so narrowing is never a
   side effect of a forgotten key. See §3.5.
4. **Budget seam** (P1T-147) — **shipped**. §3.2, applied to every agent at
   `ResolveAgentChatClient`. resume-ingestion is covered for free. Budgets are configuration
   (`AgentBudgets` in `api/Agents/appsettings.json`), not constants.
5. ~~**Convergence**~~ (P1T-148) — **landed**, except the two live re-runs. Instructions and
   descriptions now point at the Convergent Path, and §6's floor prices the whole run at **6,993**
   (1,873 × 3 calls + 87 × 2 + 1,200), inside the 8,000 target. The real-token 8,000 Cost Floor is
   committed as a live ceiling (`RosterQaConvergenceLiveFloorTests`, `Category=live`) rather than a
   deterministic one, because a real-token number cannot be measured without a model. The
   Tool-Selection Eval re-baseline is still owed.

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

- ~~**Server-side per-tool MCP scopes**~~ (P1T-149) — *shipped*, recorded in
  `manuals/mcp-tool-grants.md`. Each agent's Keycloak client carries `mcp:tool:<name>` scopes and
  the MCP server narrows `tools/list` to them and refuses `tools/call` outside them. No cost
  ceiling moves — the model is shown exactly the tools it was shown before, because the grants are
  copied from the allowlists. What it buys is that the narrowing is now a boundary rather than a
  convention: before, an agent's token was still entitled to every read tool it had filtered out
  of its own list. §3.5's closing sentence is now true.
- **resume-ingestion's tool choice** (P1T-150) — *done, §7*. It was not a tool-choice problem.

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

## 6. Convergence: pricing a run, not a call (P1T-148)

§1.4 decomposed the 160,220 tokens into payloads, and P1T-144–147 walk each payload down. But the
traced run's real defect is not in any one payload — it is that nine tool calls were made to reach
an answer two would have carried:

> three near-identical `roster_semantic_search` calls, a whole-roster `employee_list`, three
> speculative `cv_get`s, and a `roster_shortlist_search` fired *after* it already had the answer.

Every one of those payloads was inside its ceiling. A Runtime Budget truncates that run; it does
not fix it. So Convergence needs its own instrument.

### The Convergent Path

The tool sequence a converged run of a named question makes, declared in
`tools/CostFloors.Core/CostFloors.cs` and priced by `ConvergentRunCost`. For the reference
question — *"who knows react and lives in London"*:

```
skill_list("react") → roster_semantic_search(query, skillIds, location: "London") → answer
```

Three model calls. `RosterQaConvergentRunIterationCeiling` is ratcheted at 4; the traced run was 10.

`ConvergentRunCost` is Turn Amplification made arithmetic: a path of n tools is n+1 model calls,
each re-sending the Baseline Prompt Size plus every result already in hand, so the i-th result is
paid once for every call after it. Nothing is measured afresh — the baseline comes from the
`Agents.Tests` floor, the structured results from the `Mcp.Tests` floor against real Postgres, and
only the search results are pinned estimates (`ModelBackedReadToolResultEstimates`, read off §1.4's
73–1,183 range and pinned high). **So this ceiling tightens on its own as the others ratchet.**

| term | size | ×calls after | total |
|---|---|---|---|
| Baseline Prompt Size | 4,274 | ×3 | 12,822 |
| `skill_list` result | 3,080 | ×2 | 6,160 |
| `roster_semantic_search` result | 1,200 | ×1 | 1,200 |
| | | | **20,182** |

Ratcheted at 20,182 — today's price, and not a number to quote. The gap to §3.1's 6,500 is not
Convergence's: the path is already the short one, and both remaining terms belong to the tickets
ahead of it. With P1T-145's `skill_list` (≈87) and P1T-146's Baseline Prompt Size (≈1,744) the same
path prices at **≈6,600**, which is §3.1's projection reproduced from the other direction.

### What changed to get there

The filters already existed — `roster_semantic_search` takes `location`, `skillIds`, `availableOn`
and `minYears`. The model used none of them and rebuilt the location predicate by hand out of
`employee_list` and three `cv_get`s. The affordance was there; nothing pointed at it.

- **roster-qa's instructions** gained a Convergence rule ("aim to answer in two tool calls, never
  exceed four; once a result answers the question, answer and stop"), filter-first search, an
  explicit ban on rebuilding a filter from `employee_list` + `cv_get` and on re-running a search
  reworded, and "an empty result set is also an answer". Paid for out of the old prose: **416 → 415**
  estimated tokens.
- **`roster_semantic_search`'s description** now states the filters as the primary path for a
  compound question rather than listing them as an afterthought, funded by cutting elaboration that
  described other tools' return shapes: **613 → 611**, read surface 3,861 → 3,859.

### Still owed

The live confirmations, both of which need a Gemini key and free-tier quota:

- `RosterQaConvergenceLiveFloorTests` (`Category=live`) — the reference question at ≤4 real model
  calls and ≤8,000 real Gemini tokens. Note the unit: real tokens, roughly 2.4× `TokenEstimate` on
  GUID-dense payloads, so it is not comparable to the 20,182 above.
- The Tool-Selection Eval re-baseline, which the description change requires and which P1T-145
  deferred into this ticket.

## 7. resume-ingestion: 157,252 tokens, and none of the guesses were right (P1T-150)

The ledger's second-worst row — **155,668 input / 1,584 output, 13.8s**, one call — had never been
traced. This section is that trace, except it is not a trace: no model was involved, and it runs on
every push as `Agents.Tests/CostFloors/IngestionRunCostFloorTests`. The real agent is driven
through its real function-calling loop by a scripted fake client making exactly the tool calls a
faithful ingestion must make, and every model call's input is weighed.

### 7.1 The reference ingestion

The `clean-markdown` ingestion-eval fixture: well-structured, every skill already in the catalog,
nothing to self-correct. Deliberately the **easiest** fixture, so what follows is a floor under an
ingestion rather than a worst case.

Its ground truth is 8 catalog skills, 3 languages, 1 qualification, 2 roles. The write surface has
one tool per child, so a faithful ingestion is `skill_list` + `employee_create_draft` + 14 child
adds = **16 tool calls, 17 model calls**, and there is no shorter path. That is asserted against
the fixture rather than declared, so editing the fixture fails loudly instead of quietly
re-baselining everything below it.

| # | tool called after this call | adds | × re-sends | total |
|---|---|---|---|---|
| 1 | `skill_list` | 229 | 17 | 3,893 |
| 2 | `employee_create_draft` | 3,063 | 16 | **49,008** |
| 3–5 | `language_add` ×3 | 126, 39, 38 | 15, 14, 13 | 3,012 |
| 6–13 | `employee_skill_add` ×8 | 39–54 | 12…5 | 3,674 |
| 14 | `qualification_add` | 54 | 4 | 216 |
| 15–16 | `experience_add` ×2 | 64, 167 | 3, 2 | 526 |
| 17 | — (closing report) | 166 | 1 | 166 |

**111,638 estimated tokens**, and the decomposition closes exactly — the floor asserts that it
does, because a decomposition that stops closing means something is being re-sent unaccounted:

| what | share |
|---|---|
| Baseline Prompt Size (523 instructions + 6 tool schemas) × 17 calls | 51,425 — 46.1% |
| one unfiltered `skill_list` result, fetched on turn 1, re-sent 16 times | 49,008 — 43.9% |
| the resume itself | 3,893 — 3.5% |
| everything the agent actually wrote — 14 child adds, arguments and acknowledgements | 7,312 — 6.5% |

### 7.2 Three findings, and the ticket's own premise was one of the casualties

1. **The resume is 3.5%.** This ticket was opened on the assumption that ingestion is expensive
   because a pasted resume is genuinely large input — the same sentence that justified its
   40,000-token budget in §3.1. On an ordinary resume that is simply false. The bill is the loop,
   not the document.
2. **Unfiltered `skill_list` is 43.9%** — the largest single line item, exactly as it was 42% of
   the roster-qa run. But there it was the model's choice and P1T-145's `nameContains` fixed it.
   Here it is **step 1 of the agent's own instructions**: *"Call skill_list once to load the skill
   catalog."* The affordance that fixed roster-qa exists and this agent is told not to use it.
3. **The run is long by construction, and nothing recognised that.** Sixteen writes is sixteen
   iterations because the surface has one tool per child. No thrash, no retries, no speculative
   reads — the shape roster-qa was guilty of. This run does nothing wrong and still costs 111,638.

`roster_digest_list`, `employee_list` and `cv_get` are never called; the agent already narrows
itself to six tools and uses all six.

### 7.3 The budget decision

**`MaxInputTokens` stays at 40,000.** It is not generous. The per-user cap is 50,000 tokens a day
(`Usage:DefaultDailyTokens`) and it is enforced *before* a request rather than during one — which
is how a 155,668-token call was recorded under a 50,000 cap in the first place. The Runtime Budget
is therefore the only thing bounding a single run, and one resume must not cost a user their day.
The reference run does not fit inside 40,000; that is a statement about the agent's shape, not
about this number.

**`MaxIterations` goes 8 → 24.** Eight was below the agent's own structural path length, so every
ordinary resume degraded at call 8 of 17 for a reason that had nothing to do with cost. An
iteration ceiling is the backstop for a long loop of individually tiny calls (§3.2); when it sits
under the work itself it stops being a backstop and becomes the primary failure mode. 24 clears the
reference path with headroom for the ~2 retries per item the instructions allow. The token ceiling
is the one that should bind, and now it is.

This is a real raise, not a re-baseline of a Ratchet — a Runtime Budget is a production ceiling,
not a CI floor, and the two move for different reasons (§3.1).

Worth stating plainly: degrading here is less bad than it sounds. The target is a **draft** behind
the approval gate, the run service composes its counts from captured tool results rather than model
prose, and the Degradation is read off `AgentReply.Degradation` because the closing report is
schema-constrained. A truncated ingestion is an incomplete draft, honestly reported, awaiting a
human. It is still wasted work, which is why the ceiling had to move.

### 7.4 Tool Allowlist: nothing to remove

Scope item 3 asked for an allowlist once the needed tools were known from evidence. The evidence
says it is already exact. `ResumeIngestionAgent.ToolNames` narrows the `mcp:read mcp:write` surface
to six tools and the reference path calls all six, so its 3,025-token Baseline Prompt Size is the
floor for the work rather than slack — asserted, so a seventh tool cannot appear unnoticed.

What remains is enforcement, not selection: P1T-146's `McpToolSource` seam is client-side config
this agent could stop applying, and P1T-149 moves that onto the Keycloak identity. P1T-146 has
landed, and `McpAuth:resume-ingestion:Tools` mirrors `ToolNames` — `AgentToolAllowlistTests`
asserts the shipped config against `CostFloors.AgentToolAllowlists`, so the two cannot drift.

### 7.5 What was still open

Nothing above makes the run cheaper — it measures it and stops it truncating. Two levers, both
priced by the same floor, and both landed in §8:

- **Batch the children.** Issuing all adds of one kind as parallel tool calls in a single turn is
  the identical writes in the identical order, and the floor measures it at **44,001 over 7 calls
  — 61% off**, with no change to what is written. On a write loop the turn boundary is the lever,
  because iteration count multiplies both dominant terms at once.
- **Stop dumping the catalog.** Resolving each extracted skill through P1T-145's `nameContains`
  trades a 3,063-token result re-sent every turn for a handful of ~87-token lookups. Cheaper on
  tokens, but it buys them with iterations, so it is only clearly worth it *after* batching.

Both are instruction rewrites whose effect depends on what a model actually does, so both need a
live confirmation this ticket could not run — the same shape as P1T-148. That is **P1T-155**.

## 8. Turn Batching: 111,638 → 31,247, on a path that got LONGER (P1T-155)

§7 left two levers and an ordering claim: batch first, because filtering the catalog "buys tokens
with iterations". Doing both showed the ordering claim was right and its reasoning was wrong. The
lookups cost no iterations at all once the run batches, because they do not need each other's
results and go out in a single turn. Both levers land in the same place — the turn count — which
is why they compound instead of adding.

### 8.1 The two rules

Both are instruction rewrites in `ResumeIngestionAgent`; no tool, endpoint or write changed.

- **Turn Batching.** *"ONE turn per kind of call, never one per child. Calls that do not need each
  other's results go out together as parallel tool calls in a single turn; only wait for a result
  you are about to pass as an argument. Every turn re-sends the whole conversation, so ten calls
  over ten turns cost ten times what the same ten cost in one."* The rule states the mechanism, not
  just the instruction — Turn Amplification is the reason, and a model that knows the reason
  generalises it to the kinds this procedure does not enumerate.
- **Filtered lookups.** Step 1 was *"Call skill_list once to load the skill catalog"*; it is now
  one `skill_list` per skill name in the resume, `nameContains` set to the shortest distinctive
  word of it, all in one turn, and *"NEVER unfiltered"*. The affordance is P1T-145's, built for
  roster-qa, and this agent had been told not to use it.

The instruction Ratchet was **raised 523 → 663** to carry them — the second deliberate re-baseline
in this chain, after `skill_list`'s schema. It is the same trade P1T-145 made and it is not close:
140 tokens on each of 7 calls is 980, against 80,391 removed.

### 8.2 The declared shape

The reference path is now **23 tool calls**, up from 16 — eight filtered lookups where there was
one dump. Its declared turns are six:

```
8× skill_list → employee_create_draft → 3× language_add → 8× employee_skill_add
   → qualification_add → 2× experience_add → answer
```

**7 model calls, 31,247 estimated tokens.** Both are declared in `IngestionRunCost` and measured
end to end by `IngestionRunCostFloorTests`, the same instrument that priced §7.1.

| # | called after this call | adds | × re-sends | total |
|---|---|---|---|---|
| 1 | `skill_list` ×8 | 229 | 7 | 1,603 |
| 2 | `employee_create_draft` | 774 | 6 | 4,644 |
| 3 | `language_add` ×3 | 126 | 5 | 630 |
| 4 | `employee_skill_add` ×8 | 116 | 4 | 464 |
| 5 | `qualification_add` | 430 | 3 | 1,290 |
| 6 | `experience_add` ×2 | 64 | 2 | 128 |
| 7 | — (closing report) | 333 | 1 | 333 |

| what | share |
|---|---|
| Baseline Prompt Size (663 instructions + 6 tool schemas) × 7 calls | 22,155 — 70.9% |
| eight `skill_list` lookups | 4,644 — 14.9% |
| the resume itself | 1,603 — 5.1% |
| everything the agent wrote | 2,845 — 9.1% |

### 8.3 Three things this measurement says

1. **Call count is not cost; turn count is.** The path got 44% longer and the bill fell 72%. Every
   instinct that reads a tool trace and counts calls is measuring the wrong axis on a write loop.
   `CONTEXT.md` gained **Turn Batching** for exactly this, and Structural Path Length was amended:
   twenty-three calls, seven turns, and the second number is the one that bills.
2. **The levers compound.** Batching alone was 44,001 (§7.5); filtering alone, measured on the way
   through, is 103,865 over 24 calls — a 7% cut for seven more calls, barely worth doing. Together
   they are 31,247. Batching cuts how many times anything is re-sent; filtering cuts what there is
   to re-send. Neither multiplies without the other.
3. **What is left is the prompt.** 70.9% of the declared run is now the Baseline Prompt Size, and
   the remaining terms are 1,603 of resume and 2,845 of the agent's actual work. There is no third
   lever of this size: the next one is the 6 tool schemas it is shown (2,502 of the 3,165 baseline), and
   shortening those is P1T-128/129 territory, not a shape change.

### 8.4 The budget stayed put, and what is still owed

`MaxIterations` stays **24** and `MaxInputTokens` stays **40,000**. 24 is no longer the number an
ordinary run approaches — it is the backstop for a run that ignores the Batching rule, which is 24
calls exactly. Walking it down toward 7 would truncate a partially-batched run for being long
rather than for being expensive, and §3.2 is explicit that the token ceiling is the one that
should bind.

Whether an ordinary ingestion now *finishes* inside 40,000 rather than degrading is the ticket's
actual goal and this measurement **cannot answer it**: 31,247 is `TokenEstimate` tokens and
`MaxInputTokens` counts real ones, roughly 2.4× apart on the GUID-dense payloads a write loop
carries. Quoting the estimate against the budget would be the error §7.1 warns about, one section
after warning about it.

So the same thing is owed here as in §6: **`IngestionConvergenceLiveFloorTests`** (`Category=live`,
committed and unmeasured) — the reference resume at ≤11 real model calls, ≤40,000 real input
tokens, no Degradation, and every child of the ground truth actually written. That last group is
not decoration: this is the one agent holding `mcp:write`, batching changes how much the model
holds in one turn, and a cheaper run that stages a worse draft is a regression however it prices.
It writes a real draft when it runs, which is the run being measured rather than a side effect to
design away.
