namespace ExpertToJob.ExtractionEval;

/// <summary>
/// Committed floors for the extraction-fidelity eval (P1T-119) — THE place these numbers live,
/// mirroring <c>AgentEvalBaselines</c>/<c>EvalBaselines</c>. Floors are set from the first
/// measured baseline minus a noise tolerance and only move on a deliberate re-baseline; the
/// fabrication and fault ceilings are hard honesty lines, never tolerances.
///
/// <para>Baseline measured 2026-08-16 on <c>gemini-3.5-flash-lite</c> (native json_schema,
/// P1T-116 extractor), 21 golden JDs, two consecutive full runs on the frozen labels: every
/// aggregate 1.000 in both runs — concept recall, must-have precision, evidence verbatim rate,
/// seniority and location accuracy — with 0 fabrications and 0 faults. Floors sit well below
/// the measured ceiling on purpose: they gate honesty regressions and model drift, not
/// run-to-run noise. (A calibration run before freezing surfaced label bugs, not model
/// dishonesty: a must-have the JD did state, and sparse JDs honestly extracting zero
/// requirements — zero claims now scores as vacuously verbatim.)</para>
/// </summary>
public static class ExtractionEvalBaselines
{
    public const int FabricationCeiling = 0;        // honesty is a hard line
    public const int FaultCeiling = 0;              // every golden JD must extract

    public const double ConceptRecallFloor = 0.85;      // measured 0.95-0.97
    public const double MustHavePrecisionFloor = 0.80;  // measured 0.92-0.97
    public const double EvidenceVerbatimFloor = 0.75;   // measured 0.87-0.90
    public const double SeniorityAccuracyFloor = 0.90;  // measured 1.00, 1.00
    public const double LocationAccuracyFloor = 0.80;   // measured 0.90-0.95
}
