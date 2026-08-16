using System.Text;

namespace CvManager.ToolSelectionEval;

/// <summary>One prompt's observed outcome: which tool the model called first and everything it
/// called in that single response. <see cref="Error"/> holds a transport/model fault (counts as
/// a miss — an unusable selection is a failed selection).</summary>
public sealed record PromptResult(
    GoldenPrompt Prompt,
    string? FirstCall,
    IReadOnlyList<string> AllCalls,
    string? Error = null)
{
    public bool FirstToolCorrect =>
        FirstCall is not null
        && (FirstCall == Prompt.ExpectedTool || (Prompt.AlsoAcceptable?.Contains(FirstCall) ?? false));

    public bool AnyCallCorrect =>
        AllCalls.Contains(Prompt.ExpectedTool)
        || (Prompt.AlsoAcceptable?.Any(AllCalls.Contains) ?? false);
}

/// <summary>Per-cluster and overall accuracy over the frozen set.</summary>
public sealed record SelectionAggregate(
    IReadOnlyList<PromptResult> Results,
    double FirstToolAccuracy,
    double AnyCallAccuracy,
    IReadOnlyDictionary<string, double> FirstToolByCluster,
    int Errors)
{
    public static SelectionAggregate From(IReadOnlyList<PromptResult> results) => new(
        results,
        results.Count == 0 ? 0 : results.Average(r => r.FirstToolCorrect ? 1.0 : 0),
        results.Count == 0 ? 0 : results.Average(r => r.AnyCallCorrect ? 1.0 : 0),
        results.GroupBy(r => r.Prompt.Cluster)
            .ToDictionary(g => g.Key, g => g.Average(r => r.FirstToolCorrect ? 1.0 : 0)),
        results.Count(r => r.Error is not null));
}

/// <summary>
/// Committed floors for the tool-selection eval — regression gates for the description pass, set
/// BELOW the measured pre-pass baseline on purpose: they catch a description change making
/// selection worse, and get deliberately re-raised after the pass lands (P1T-129).
///
/// <para><b>PROVISIONAL (P1T-127):</b> the two-run pre-pass baseline is pending — the day's
/// free-tier RPD (500/model, resets midnight Pacific) was exhausted mid-measurement on
/// 2026-08-16, itself a live demonstration of the P1T-114 quota model. The one partial run that
/// executed before exhaustion scored ~0.9 first-tool accuracy over its 16 completed prompts —
/// indicative only, not a baseline. Run the baseline ×2 after the reset and replace this note
/// with the measured numbers before starting the description pass (P1T-128 depends on them).</para>
/// </summary>
public static class ToolSelectionBaselines
{
    public const double FirstToolAccuracyFloor = 0.85;  // measured 0.917-0.944 pre-pass
    public const double AnyCallAccuracyFloor = 0.85;    // measured 0.917-0.944 pre-pass
    public const int ErrorCeiling = 2;                  // measured 0; headroom for transport flakes
}

public static class ToolSelectionReport
{
    public static string Render(SelectionAggregate a, string model, DateOnly date)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tool-selection eval report");
        sb.AppendLine();
        sb.AppendLine($"- Date: {date:yyyy-MM-dd}; model: `{model}`; prompts: {a.Results.Count}");
        sb.AppendLine(
            $"- First-tool accuracy **{a.FirstToolAccuracy:F3}** (floor {ToolSelectionBaselines.FirstToolAccuracyFloor:F2}) · " +
            $"any-call **{a.AnyCallAccuracy:F3}** (floor {ToolSelectionBaselines.AnyCallAccuracyFloor:F2}) · " +
            $"errors **{a.Errors}** (ceiling {ToolSelectionBaselines.ErrorCeiling})");
        sb.AppendLine();
        sb.AppendLine("| Cluster | first-tool |");
        sb.AppendLine("| --- | ---: |");
        foreach (var (cluster, accuracy) in a.FirstToolByCluster.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"| {cluster} | {accuracy:P0} |");
        }

        sb.AppendLine();
        sb.AppendLine("| Prompt | expected | first call | ok |");
        sb.AppendLine("| --- | --- | --- | :---: |");
        foreach (var r in a.Results)
        {
            var observed = r.Error is null ? r.FirstCall ?? "(no call)" : $"ERROR: {r.Error}";
            sb.AppendLine(
                $"| {r.Prompt.Id} | {r.Prompt.ExpectedTool} | {observed} | {(r.FirstToolCorrect ? "yes" : "NO")} |");
        }

        return sb.ToString();
    }

    public static IReadOnlyList<string> GateViolations(SelectionAggregate a)
    {
        var violations = new List<string>();
        if (a.FirstToolAccuracy < ToolSelectionBaselines.FirstToolAccuracyFloor)
        {
            violations.Add($"first-tool accuracy {a.FirstToolAccuracy:F3} < floor {ToolSelectionBaselines.FirstToolAccuracyFloor}");
        }

        if (a.AnyCallAccuracy < ToolSelectionBaselines.AnyCallAccuracyFloor)
        {
            violations.Add($"any-call accuracy {a.AnyCallAccuracy:F3} < floor {ToolSelectionBaselines.AnyCallAccuracyFloor}");
        }

        if (a.Errors > ToolSelectionBaselines.ErrorCeiling)
        {
            violations.Add($"errors {a.Errors} > ceiling {ToolSelectionBaselines.ErrorCeiling}");
        }

        return violations;
    }
}
