# JD Requirement Extraction — honest structured schemas end to end

One extraction per job description is the single source of requirements for every agent surface
(P1T-111 design, built as P1T-115…P1T-120). The schema is honest by construction — "not stated
in the JD" is representable and badged, never fabricated — and the honesty is *measured*, not
assumed, by a fabrication-gated eval.

> Decision trail: wayfinder map **P1T-105** — research **P1T-106** (Anthropic → Gemini/IChatClient
> mapping, see [`anthropic-gemini-ichatclient-mapping.md`](anthropic-gemini-ichatclient-mapping.md)),
> grilling **P1T-111** (method + schema), tool-reliability grilling **P1T-112**.
> Build slices: **P1T-115** (model pin + compat probes) → **P1T-116** (extractor) → **P1T-118**
> (Match/ResumeIngestion/narrative conversions) → **P1T-117** (consumer wiring) → **P1T-119**
> (fidelity eval) → **P1T-120** (UI badges + this doc).

## Method: native json_schema on the compat wire

The P1T-115 live probes (`tests/Agents.Tests/CompatEndpointProbeTests.cs`, kept as regression
canaries) verified what the Gemini OpenAI-compat endpoint actually honors on the pinned
`gemini-3.5-flash-lite`:

| Knob | Result |
| --- | --- |
| `GetResponseAsync<T>` with `useJsonSchemaResponseFormat: true` (native `response_format` json_schema) | PASS — the chosen method |
| The documented fallback (`false` = JSON mode + schema injected into the prompt) | PASS — kept probe-covered, not the default |
| `ChatToolMode.RequireAny` / `RequireSpecific` forcing | PASS (undocumented on the compat wire) |
| Structured output **combined with tools** in one request | PASS — what made converting tool-carrying agents (Match, ResumeIngestion) safe |

The model is pinned explicitly (`gemini-3.5-flash-lite`) because free-tier quotas are per model
row and a `-latest` alias can drift onto an RPD-20 row (P1T-114: 3.5-flash-lite RPM 15 / RPD 500;
every Flash-proper row RPM 5 / RPD 20).

## The schema (`api/Agents/Agents/JdRequirements.cs`)

```
JdRequirements {
  requirements: [{ text, kind: Skill|Experience|Qualification|Language|Availability|Location|Other,
                   priority: MustHave|NiceToHave|Unspecified, minYears: int|null,
                   evidenceSpan: verbatim JD quote|null, inferred: bool }],
  seniority: Junior|Mid|Senior|Lead|Principal|Unspecified,
  location: string|null,
  ambiguities: [string]
}
```

Honesty rules (enforced by prompt, verified by code, measured by the eval):

- Every enum carries **Unspecified**; `minYears`/`location` are nullable — silence round-trips
  as silence.
- **Evidence Span**: every requirement carries the verbatim JD quote that states it, verified by
  `JdRequirementExtractor` against the JD (collapse-whitespace, case-insensitive containment —
  the interview-kit rule). Unverifiable or missing → **`inferred: true`, kept and badged** —
  never silently stripped, never silently trusted. A model-declared `inferred` is never
  downgraded by a coincidentally matching quote.
- **`ambiguities[]`** is the model's explicit outlet for "the JD is unclear about X" — instead
  of guessing.
- The extractor is **tool-less** (no agent identity, no MCP), which sidesteps the structured-
  output-with-tools constraint entirely and keeps the call cheap.

## Consumer map — one extraction per JD

| Consumer | How it consumes |
| --- | --- |
| **Shortlist** (`ShortlistRunService`) | Orchestrates extract → **deterministic** `roster_shortlist_search` invoke with the extracted texts (`IShortlistSearch`; the model no longer picks tool arguments) → tool-less schema-constrained rationale call. The full extraction rides the response as the additive `extraction` field. |
| **Staffing pipeline** | The shortlist step's extraction flows down the stage DTOs into every match run and onto the report (`StaffingReport.extraction`); no per-step re-extraction. |
| **Match** (`IMatchRunService`) | The extraction's prompt block (`JdRequirements.ToPromptBlock()`) rides into the match prompt; the one-shot endpoint extracts once itself. Match's own verdict is structured too (`MatchAssessment {score, band, gapAnalysisMarkdown}`, P1T-118) — `MatchAnswerParser` is fallback-only. |
| **Interview Kit** | The endpoint extracts once and appends the prompt block for gap-targeting. |
| **Roster Scan** (planned, P1T-110) | Inherits the extraction and the structured-output method. |
| **UI** (`web/src/components/agent/RequirementChips.tsx`) | "How the JD was read" chips: must-have color, `· N+ yrs` labels, an **inferred** marker with tooltip, evidence-span tooltips, and the ambiguities note. Falls back to plain strings when the payload has no extraction. |

An extraction fault never fails a call: Shortlist degrades it as the run fault (metered first),
Match/Interview Kit degrade to a plain-JD run. Extraction tokens meter under the
**`jd-extraction`** agent name everywhere, so the Usage tab's per-agent breakdown stays truthful.

## Measured, not guessed: the extraction-fidelity eval (P1T-119)

`tools/ExtractionEval` (CLI, exit code = gate) + `ExtractionFidelityEvalTests` (live regression
gate) run the real extractor over a frozen golden set of **21 hand-labeled JDs** — rich across
domains, deliberately sparse, and tricky/ambiguous honesty cases. Floors live in
`tools/ExtractionEval.Core/ExtractionEvalBaselines.cs`.

The **fabrication ladder**: a priority over-claim on a stated concept is a precision miss; a
must-have with no basis in the JD, or any invented value on a silent slot (seniority, location,
years), is a **hard-gated fabrication — ceiling 0**.

Baseline (2026-08-16, pinned model, two consecutive runs on the frozen labels): **1.000 on every
aggregate — concept recall, must-have precision, evidence verbatim rate, seniority and location
accuracy — 0 fabrications, 0 faults.** Floors are committed below the ceiling on purpose: they
gate honesty regressions and model drift, not run-to-run noise. The pre-freeze calibration run
surfaced label bugs, not model dishonesty — notably, sparse JDs ("Engineer wanted.") honestly
extract *zero* requirements, which now scores as vacuously verbatim.

Run it:

```bash
GEMINI_API_KEY=<key> dotnet run --project tools/ExtractionEval -- [--output report.md] [--delay 5]
GEMINI_API_KEY=<key> dotnet test tests/Agents.Tests --filter "FullyQualifiedName~ExtractionFidelityEval"
```

## Related conversions (P1T-118)

The same native-json_schema knob retired the lenient-parser class everywhere a schema already
existed: Match's verdict, ResumeIngestion's closing report, and the staffing narrative are all
schema-constrained on the wire, with the old lenient parsers kept as fallbacks and the semantic
corruption guards (unknown-id drop, must-name-a-candidate) intact — schema validity is not
semantic validity.
