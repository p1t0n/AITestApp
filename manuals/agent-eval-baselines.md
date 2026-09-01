# Agent eval baselines (P1T-97) and Cost Floors (P1T-144)

Two live evals guard the model-facing halves of the agent stack, following the retrieval-eval
precedent (`manuals/retrieval-eval-baseline.md`): committed floors in
`tests/Agents.Tests/Eval/AgentEvalBaselines.cs`, live runs behind `Category=eval`, the default
`dotnet test` run stays model-free (the tests skip without a key).

```bash
GEMINI_API_KEY=<key> dotnet test tests/Agents.Tests --filter "Category=eval"
```

A third live eval, the **tool-selection eval** (P1T-127), sits in `tests/Mcp.Tests` rather than
here because it measures the MCP tool surface, not an agent: its floors and the description pass's
before/after live in `manuals/mcp-tool-descriptions.md`
(`dotnet test tests/Mcp.Tests --filter "Category=eval"`).

Run on demand and before merging changes to agent instructions, extraction contracts, or the
model choice. A floor failure is a hard test failure — re-baseline deliberately, never by
loosening a floor to make a red run pass.

## 1. Ingestion extraction (`IngestionExtractionEvalTests`)

Graduated from the P1T-81 gate prototype. Runs the **real `ResumeIngestionAgent`** — production
instructions, production self-correction behavior — against the real model. The MCP surface is
faked (`IngestionEvalTools`): same tool names and result shapes as the server, validation through
the **real Application validators** (so the self-correction loop sees production error shapes),
every staged write recorded and scored against 8 hand-written ground-truth resumes
(`IngestionEvalFixtures`: clean markdown, LinkedIn dump, terse, messy formatting, career changer,
missing email, non-catalog skills, date traps).

Metrics: expert field accuracy, catalog-skill recall/precision (via the written skill ids),
hallucinated skills (written but neither true nor mentioned), fabricated emails (the honesty hard
line — a resume without an address must stage an empty one), experience match + date errors,
language/qualification recall, validation-rejection count (self-correction pressure).

## 2. Requirement extraction (`RequirementExtractionEvalTests`)

The shortlist agent's first duty — distilling a JD into 3-8 requirement phrases — feeds the
retrieval tool, so drift here poisons shortlist AND staffing. Runs the **real `ShortlistAgent`**
against the real model with a fake `roster_shortlist_search` capturing the requirement strings the
model actually passed. 10 hand-built JDs (`react-senior` … `embedded-firmware`), each with the
capability concepts a faithful reading must surface (keyword-alternative groups).

Metrics: concept coverage (recall), phrase precision (each produced requirement must trace back to
the JD), and the 3-8 count band from the agent contract.

## Baseline (measured 2026-08-01, `gemini-flash-lite-latest`)

Two full runs on the baseline day; floors sit below the observed minimum (see
`AgentEvalBaselines.cs`). Skill recall counts catalog-AVAILABLE truth skills only — non-catalog
skills correctly become proposals (the noncatalog fixture proposed all six of its specialist
skills and wrote only Python).

| Eval | Metric | Measured | Floor |
|---|---|---|---|
| Ingestion | field accuracy | 1.00 / 1.00 | 0.90 |
| Ingestion | skill recall / precision | ~0.97 / 1.00 | 0.85 / 0.90 |
| Ingestion | hallucinated skills / fabricated emails | 0 / 0 | 0 / 0 (ceilings) |
| Ingestion | experience match / date errors | 0.81–1.00 / 0–1 | 0.75 / ≤2 |
| Requirements | concept coverage | 0.93–1.00 | 0.80 |
| Requirements | phrase precision | 0.98–1.00 avg | 0.85 |
| Requirements | count band 3-8 | 10/10 both runs | 10/10 |

Known variance: the career-changer fixture's teaching role is sometimes not staged as an
experience; the LinkedIn fixture's WCAG skill sometimes lands as a proposal instead of the
catalog match. Both are judgment calls, not honesty failures — the honesty ceilings (zero
hallucinated skills, zero fabricated emails) held in every observed run.


## 4. Cost Floors (P1T-144)

The evals above are live and opt-in, which is exactly why a 27× cost regression shipped green
through four tickets (`manuals/agent-cost-budgets.md`). The Cost Floors are the other half: they
run on **every push**, involve **no model at all**, and guard the two things that actually
regressed — the size of the tool surface agents are shown, and the size of what read tools return.

```bash
dotnet test tests/Mcp.Tests    --filter "FullyQualifiedName~CostFloors"
dotnet test tests/Agents.Tests --filter "FullyQualifiedName~CostFloors"
```

All ceilings live in `tools/CostFloors.Core/CostFloors.cs` — one table, shared by both test
projects because an agent's Baseline Prompt Size is its own instructions (measurable only in
`Agents.Tests`) plus the schemas of the tools it is shown (measurable only in `Mcp.Tests`).

### The unit

`TokenEstimate` — four characters per estimated token. These are **estimated tokens, not Gemini
tokens**, and the gap is not small: the traced run charged 7,522 real input tokens for the
`skill_list` result that this estimator calls 3,080. GUID-dense JSON tokenizes far worse than
prose, so real cost runs ~2.4× the estimate on result payloads and close to 1× on descriptions
(roster-qa's pre-P1T-146 Baseline Prompt Size: 4,277 estimated vs 4,202 real). A Ratchet only needs to be
stable and proportional; never quote an estimate as a bill.

### Ratchet, not target

Every ceiling is pinned at the value measured on 2026-08-30 and may only ever move **down**. Main
stays green through the whole chain, and each later ticket lands a visible numeric delta:
tighten the ceiling (red), implement (green). Raising one is a deliberate re-baseline with a
reason on the issue — never the fix for a red run.

### What is measured

| Floor | Where | How |
|---|---|---|
| Read-tool result size | `Mcp.Tests/CostFloors/ReadToolResultCostFloorTests` | Real Postgres (Testcontainers, pgvector), the first 45 demo-roster experts over the full 79-skill catalog — the roster shape the 2026-08-30 measurement ran on |
| Per-tool schema size | `Mcp.Tests/CostFloors/ToolSurfaceCostFloorTests` | The MCP tool listing itself: name + description + input schema |
| Read surface total | same | Sum over the 11 `mcp:read` tools — the pool every Tool Allowlist is drawn from |
| Agent instruction size | `Agents.Tests/CostFloors/BaselinePromptSizeFloorTests` | The authored `Instructions` prompt of all 9 prompted agents |
| Baseline Prompt Size | same | Instructions + the pinned schema size of every tool the agent actually hands the model, driven through the real agent with a fake chat client and the agent's own Tool Allowlist as the offered surface |
| Tool Allowlist | `Agents.Tests/AgentToolAllowlistTests` | The shipped `appsettings.json` asserted against `CostFloors.AgentToolAllowlists` — the same declaration the Baseline Prompt Size floor measures against, so config and cost cannot drift apart |
| Tool Grants | `Mcp.Tests/KeycloakToolGrantTests` | The shipped `keycloak/realm-export.json` asserted against the same declaration (P1T-149): the `mcp:tool:*` scopes on each agent client are the boundary the server enforces, so the measured surface and the entitled one are one set. Deterministic — the realm export is JSON on disk, no Keycloak needed |
| Convergent run | `Agents.Tests/CostFloors/ConvergenceCostFloorTests` | The whole reference question priced along its declared Convergent Path — Baseline Prompt Size × model calls plus each result × the calls that follow it (P1T-148, `manuals/agent-cost-budgets.md` §6) |

Two coverage guards keep the floors from rotting: a read tool with no result ceiling fails unless
it is listed in `ModelBackedReadTools` (it embeds a query, so it cannot be measured model-free),
and the `ReadScopeTools` / `WriteScopeTools` declarations are asserted against what the server
really advertises per scope.

### Measured 2026-08-30 (estimated tokens)

Read-tool results, 45-expert demo roster:

| Tool | Ceiling | Note |
|---|---|---|
| `roster_digest_list` | 18,300 | one default page of 50; the only ceiling with slack (see below) |
| `skill_list` (`nameContains: "React"`) | 87 | was 3,080 and 42% of the traced roster-qa run; **P1T-145** ratcheted it onto the lookup that run wanted |
| `skill_list` (unfiltered) | 3,100 | the sweep half, ratcheted separately as `SkillListUnfilteredPageCeiling` |
| `category_tree` | 3,379 | |
| `expert_list` | 2,805 | 12.7% of the same run |
| `expert_get` | 2,064 | |
| `cv_get` | 1,643 | |
| `category_list` | 270 | |
| `availability_list` | 73 | |

`roster_digest_list` is the one ceiling carrying slack (measured 18,248–18,253). Digests truncate
at 1,500 **characters** and EF leaves an expert's experience order unspecified, so which
non-ASCII characters fall inside the cut — and therefore how many `\uXXXX` escapes the JSON
carries — shifts a few tokens per seed. Every other ceiling is pinned exactly.

Baseline Prompt Size:

| Agent | Instructions | Tool schemas | Baseline | Note |
|---|---|---|---|---|
| roster-qa | 415 | 1,458 (4 of 11 read tools) | 1,873 | was 4,409 on all 11 — ratcheted by **P1T-146**, then **P1T-148**'s instruction and description rewrites |
| resume-ingestion | 523 | 2,502 (6, incl. writes) | 3,025 | the one agent holding `mcp:write` (**P1T-150**) |
| cv-tailoring | 498 | 689 (`style_exemplar_search`) | 1,187 | `cv_get` is deterministic, not shown to the model |
| interview-kit | 371 | 296 (`cv_get`) | 667 | |
| match | 328 | 296 (`cv_get`) | 624 | |

Instructions only (no tools reach the model): shortlist 121, bench-report 199, roster-scan
scorer 199, JD-requirement extractor 237.

The read surface totals **3,962** across 11 tools; the widest single schemas are
`style_exemplar_search` 689, `roster_shortlist_search` 635 and `roster_semantic_search` 611. Since
P1T-146 no agent is shown all of it — each identity's Tool Allowlist is declared in
`CostFloors.AgentToolAllowlists` and configured under `McpAuth:<agent>:Tools`.

### Ingestion Run Cost (P1T-150)

The floors above price one *call*. `Agents.Tests/CostFloors/IngestionRunCostFloorTests` prices a
whole **run**, for the one agent whose run is long by construction: the real
`ResumeIngestionAgent` is driven through its real function-calling loop by a scripted fake client
making exactly the calls a faithful ingestion of the `clean-markdown` eval fixture must make, with
realistic arguments, and every model call's input is weighed. Ceilings live in
`tools/CostFloors.Core/IngestionRunCost.cs`.

Only two terms are composed rather than measured — the instruction and tool-schema ceilings above
— and `skill_list`'s result is the one synthesized payload, held under the ceiling `Mcp.Tests`
measures against real Postgres so the run can only be priced conservatively.

| Shape | Model calls | Estimated tokens | Baseline Prompt Size | unfiltered `skill_list` | resume | what it wrote |
|---|---|---|---|---|---|---|
| serial — one tool call per turn | 17 | **111,638** | 51,425 (46.1%) | 49,008 (43.9%) | 3,893 (3.5%) | 7,312 (6.5%) |
| batched — one turn per child kind | 7 | **44,001** | 21,175 (48.1%) | 18,378 (41.8%) | 1,603 (3.6%) | 2,845 (6.5%) |

The decomposition closes exactly and is asserted to: every token is either the Baseline Prompt
Size or a conversation addition times the calls that re-send it.

Read it against the assumption it replaces. Ingestion was thought expensive because a pasted
resume is large input — the resume is **3.5%**. It is expensive because sixteen writes is sixteen
iterations, each re-sending a 3,025-token prompt surface and a 3,063-token catalog dump nobody
filtered. Same two line items as the roster-qa regression, on an agent that was never traced.

### The one raised Ratchet: `skill_list`'s schema, 176 → 308 (P1T-145)

A Ratchet may only move down, so this is called out rather than buried. Teaching a tool a filter
is not free: three optional parameters and the sentence that explains them cost **+132 estimated
tokens per iteration**, and the read-surface total moved 3,861 → 3,993 with them.

It is bought back many times over. On the traced run the schema half would have cost +1,320
(132 × 10 iterations) and the result half saves 26,937 (2,993 × 9 re-sends) — the payload that was
42% of the bill drops to two rows. The general lesson for the rest of this chain: **schema tokens
are cheap relative to result tokens**, because a result is both large and re-sent, so an
affordance that shrinks results is worth paying description for. The reverse trade — a longer
description that does not change what comes back — is not.

### Convergent run (P1T-148)

The reference question *"who knows react and lives in London"* prices at **6,984** along
`skill_list` → `roster_semantic_search` → answer: 1,873 × 3 model calls, plus 87 × 2 and 1,200 × 1
for the results each following call re-sends. Its iteration ratchet is **4** model calls; the
traced run took 10. This ceiling is composed from the two tables above rather than pinned
independently, so it tightens whenever they do — re-read it off the test output, never re-derive
it by hand:

```bash
dotnet test tests/Agents.Tests --filter "FullyQualifiedName~ConvergenceCostFloorTests"
```

The real-token half of the same floor — ≤4 model calls at ≤8,000 **Gemini** tokens — is
`RosterQaConvergenceLiveFloorTests`, opt-in behind `Category=live` because it needs a key and a
running MCP server + Keycloak. Estimated and real tokens are different units (≈2.4× apart on
GUID-dense payloads); never compare the two ceilings.
