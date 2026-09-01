namespace ExpertToJob.CostFloors;

/// <summary>
/// The deterministic token yardstick the Cost Floors are measured in (P1T-144).
///
/// <para>A Cost Floor has to run on every push, which rules out asking a model how many tokens
/// something is. So it estimates: four characters per estimated token, the usual rule of thumb.</para>
///
/// <para>These are ESTIMATED tokens, not Gemini tokens, and the gap is not small. The one traced
/// run in <c>manuals/agent-cost-budgets.md</c> charged 7,522 real input tokens for
/// <c>skill_list</c>'s result; this estimator calls the same payload 3,080. GUID-dense JSON
/// tokenizes far worse than prose, so real cost runs roughly 2.4× the estimate on those payloads
/// and closer to 1× on descriptions. Never quote an estimate as a bill.</para>
///
/// <para>The absolute number is not what a Ratchet is for: it only has to be stable and
/// proportional, so that a description that doubles or a result that stops paging shows up as a
/// red test. Do NOT change the divisor to "improve accuracy" — every committed ceiling is
/// denominated in it, and moving it silently re-baselines all of them at once.</para>
/// </summary>
public static class TokenEstimate
{
    /// <summary>Characters per estimated token.</summary>
    public const double CharsPerToken = 4.0;

    /// <summary>Estimated tokens for one payload.</summary>
    public static int Of(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);
}
