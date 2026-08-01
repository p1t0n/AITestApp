# Agent eval baselines (P1T-97)

Two live evals guard the model-facing halves of the agent stack, following the retrieval-eval
precedent (`manuals/retrieval-eval-baseline.md`): committed floors in
`tests/Agents.Tests/Eval/AgentEvalBaselines.cs`, live runs behind `Category=eval`, the default
`dotnet test` run stays model-free (the tests skip without a key).

```bash
GEMINI_API_KEY=<key> dotnet test tests/Agents.Tests --filter "Category=eval"
```

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

_(numbers recorded from the first committed baseline run — see `AgentEvalBaselines.cs` for the
floors derived from them)_

| Eval | Metric | Measured | Floor |
|---|---|---|---|
| Ingestion | field accuracy | TBD | 0.90 |
| Ingestion | skill recall / precision | TBD | 0.85 / 0.90 |
| Ingestion | hallucinated skills / fabricated emails | TBD | 0 / 0 (ceilings) |
| Ingestion | experience match / date errors | TBD | 0.90 / ≤2 |
| Requirements | concept coverage | TBD | 0.80 |
| Requirements | phrase precision | TBD | 0.85 |
| Requirements | count band 3-8 | TBD | 10/10 |
