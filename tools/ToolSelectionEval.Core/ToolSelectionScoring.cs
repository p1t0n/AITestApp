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
/// Committed floors for the tool-selection eval. Measured, not guessed — see
/// <c>manuals/mcp-tool-descriptions.md</c> for the before/after table.
///
/// <para><b>Pre-pass baseline (P1T-127, measured 2026-08-28, <c>gemini-3.5-flash-lite</c>):</b>
/// two runs, first-tool 0.821 both times, 0 errors; per cluster
/// capability/exact-fact/bulk-sweep/catalog 100%, shortlist 75%, writes 75%, style 0%.</para>
///
/// <para><b>Post-pass (P1T-128 read-cluster rewrite):</b> four runs — 0.846, 0.846, 0.821, 0.821,
/// 0 errors. capability/exact-fact/bulk-sweep/catalog held at 100% in every run; style stayed 0%
/// and writes 75% throughout. The shortlist cluster is the one that moved and the one that is
/// unstable: its <c>sl-jd-paste</c> prompt never picked <c>employee_list</c> again (its consistent
/// pre-pass miss), choosing <c>roster_shortlist_search</c> twice and <c>roster_digest_list</c>
/// twice — selection moved into the roster-search family but is not pinned. Selection on this
/// model is NOT deterministic; two identical runs are a coincidence, not proof.</para>
///
/// <para>Floors are therefore set one prompt below the MINIMUM observed, never from a run pair
/// that happened to agree: global 0.80 (min observed 0.821 = 32/39, so 31/39 trips it), clusters
/// at the lowest figure each held across all runs. style is knowingly ungated — its misses are an
/// affordance problem (a required <c>achievementIds</c> argument the prompts cannot supply), not a
/// wording one, and a floor nobody can hold teaches nothing. Raise these deliberately when
/// P1T-129 lands; never lower one to make a red run pass.</para>
/// </summary>
public static class ToolSelectionBaselines
{
    public const double FirstToolAccuracyFloor = 0.80;  // min observed 0.821 (post-pass 0.846/0.846/0.821/0.821)
    public const double AnyCallAccuracyFloor = 0.80;    // tracks first-tool on this set
    public const int ErrorCeiling = 2;                  // measured 0; headroom for transport flakes

    /// <summary>Per-cluster first-tool floors — the sharp instrument: a careless edit to one
    /// description trips its own cluster long before it moves a 39-prompt average. Each floor is
    /// the lowest figure the cluster held across every measured run, so it gates regression, not
    /// variance. style is absent on purpose (0% pre and post).</summary>
    public static readonly IReadOnlyDictionary<string, double> ClusterFirstToolFloors =
        new Dictionary<string, double>
        {
            [GoldenPromptSet.Capability] = 1.0,
            [GoldenPromptSet.ExactFact] = 1.0,
            [GoldenPromptSet.BulkSweep] = 1.0,
            [GoldenPromptSet.Catalog] = 1.0,
            [GoldenPromptSet.Shortlist] = 0.75,
            [GoldenPromptSet.Writes] = 0.75,
        };
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
        sb.AppendLine("| Cluster | first-tool | floor |");
        sb.AppendLine("| --- | ---: | ---: |");
        foreach (var (cluster, accuracy) in a.FirstToolByCluster.OrderBy(kv => kv.Key))
        {
            var floor = ToolSelectionBaselines.ClusterFirstToolFloors.TryGetValue(cluster, out var f)
                ? f.ToString("P0")
                : "—";
            sb.AppendLine($"| {cluster} | {accuracy:P0} | {floor} |");
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

        // A cluster absent from the run is not scored — the gate never invents a measurement.
        foreach (var (cluster, floor) in ToolSelectionBaselines.ClusterFirstToolFloors)
        {
            if (a.FirstToolByCluster.TryGetValue(cluster, out var measured) && measured < floor)
            {
                violations.Add($"cluster '{cluster}' first-tool {measured:F3} < floor {floor}");
            }
        }

        return violations;
    }
}
