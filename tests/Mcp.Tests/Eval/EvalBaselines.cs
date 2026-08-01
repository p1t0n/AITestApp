namespace CvManager.Mcp.Tests.Eval;

/// <summary>
/// Committed retrieval-quality baselines the live regression test asserts against. This is THE
/// place baseline numbers live.
///
/// <para>Measured 2026-07-11 with <c>text-embedding-3-small</c> over the frozen 24-employee corpus
/// and 39-query golden set at the production threshold 0.30 (see
/// <c>manuals/retrieval-eval-baseline.md</c> for the full sweep): recall@5 = 1.0000,
/// MRR = 0.9848, negative-FP rate = 0.0000. Re-measure with
/// <c>dotnet run --project tools/RetrievalEval -- --threshold 0.30</c>.</para>
/// </summary>
public static class EvalBaselines
{
    /// <summary>Measured recall@5 floor at the production threshold (2026-07-11 run).</summary>
    public const double RecallAt5 = 1.0;

    /// <summary>Slack subtracted from the baseline before asserting, absorbing run-to-run noise.</summary>
    public const double Tolerance = 0.05;
}
