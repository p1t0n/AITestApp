namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Committed retrieval-quality baselines the live regression test asserts against. This is THE
/// place baseline numbers live.
///
/// <para>Measured 2026-08-01 with <c>gemini-embedding-001</c> (1536 dims) over the frozen
/// 24-expert corpus and 39-query golden set at the production threshold 0.55 (see
/// <c>manuals/retrieval-eval-baseline.md</c> for the full sweep — the 0.30 floor tuned for the
/// retired OpenAI model let every negative query through on Gemini): recall@5 = 1.0000,
/// MRR = 1.0000, negative-FP rate = 0.0000. Re-measure with
/// <c>dotnet run --project tools/RetrievalEval -- --threshold 0.55</c>.</para>
/// </summary>
public static class EvalBaselines
{
    /// <summary>Measured recall@5 floor at the production threshold (2026-08-01 Gemini run).</summary>
    public const double RecallAt5 = 1.0;

    /// <summary>Slack subtracted from the baseline before asserting, absorbing run-to-run noise.</summary>
    public const double Tolerance = 0.05;
}
