# Agent eval baselines (P1T-97)

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

Metrics: employee field accuracy, catalog-skill recall/precision (via the written skill ids),
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
