namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Committed retrieval-quality baselines the live regression test asserts against. This is THE
/// place baseline numbers live.
///
/// <para>PLACEHOLDER: slice B2 (P1T-52) will run the eval against the real embedding backend and
/// commit the measured numbers here. Until then <see cref="RecallAt5"/> is a deliberately
/// conservative floor — real recall is expected to be well above it — so the gate catches gross
/// regressions without flaking on model noise.</para>
/// </summary>
public static class EvalBaselines
{
    /// <summary>Conservative placeholder floor for recall@5 (see class remarks; P1T-52 replaces it).</summary>
    public const double RecallAt5 = 0.5;

    /// <summary>Slack subtracted from the baseline before asserting, absorbing run-to-run noise.</summary>
    public const double Tolerance = 0.05;
}
