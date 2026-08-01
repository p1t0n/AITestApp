namespace CvManager.Agents.Tests.Eval;

/// <summary>
/// Committed baseline floors for the live agent evals (P1T-97) — THE place these numbers live,
/// mirroring <c>tests/Mcp.Tests/Eval/EvalBaselines.cs</c>. Floors are set from the first measured
/// baseline run minus a noise tolerance, and only move on a deliberate re-baseline.
///
/// <para>Measured 2026-08-01 on <c>gemini-flash-lite-latest</c>, two full runs. Ingestion
/// (8 fixtures, real agent instructions + real validators behind fake MCP tools): field accuracy
/// 1.00 both runs; skill recall 0.97 (catalog-available skills; non-catalog ones correctly became
/// proposals — LabVIEW, COBOL, SCADA…); precision 1.00; hallucinated skills 0; fabricated emails
/// 0; date errors 0-1; experience match varied 0.81-1.00 (the career-changer's teacher role is
/// sometimes not staged as an experience — floor set below the observed minimum). Requirement
/// extraction (10 JDs): concept coverage 0.93-1.00, phrase precision 0.83-1.00 avg 0.98+, all runs
/// inside the 3-8 band. Re-measure:
/// <c>GEMINI_API_KEY=&lt;key&gt; dotnet test tests/Agents.Tests --filter "Category=eval"</c>.</para>
/// </summary>
public static class AgentEvalBaselines
{
    // ---- Ingestion extraction (per-run aggregates over the 8 fixtures) ----
    public const double IngestionFieldAccuracyFloor = 0.90;   // measured 1.00, 1.00
    public const double IngestionSkillRecallFloor = 0.85;     // measured ~0.97 (catalog-available)
    public const double IngestionSkillPrecisionFloor = 0.90;  // measured 1.00, 1.00
    public const int IngestionHallucinatedSkillsCeiling = 0;  // honesty is a hard line
    public const int IngestionFabricatedEmailsCeiling = 0;    // honesty is a hard line
    public const double IngestionExperienceMatchFloor = 0.75; // measured 0.81-1.00 (run variance)
    public const int IngestionDateErrorsCeiling = 2;          // measured 0-1

    // ---- Requirement extraction (per-run aggregates over the 10 JDs) ----
    public const double RequirementCoverageFloor = 0.80;      // measured 0.93-1.00
    public const double RequirementPrecisionFloor = 0.85;     // measured 0.98-1.00 avg
    public const int RequirementCountMin = 3;                 // contract band from the agent prompt
    public const int RequirementCountMax = 8;
}
