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
/// <para><b>After pass 1 (P1T-128, read clusters):</b> four runs — 0.846, 0.846, 0.821, 0.821, 0
/// errors. capability/exact-fact/bulk-sweep/catalog held at 100% in every run; style stayed 0% and
/// writes 75% throughout. Selection on this model is NOT deterministic; two identical runs are a
/// coincidence, not proof.</para>
///
/// <para><b>After pass 2 (P1T-129, the write surface):</b> three runs — 0.846, 0.872, 0.821, 0
/// errors. writes read 83%, 83%, 75% (its gain: <c>write-experience</c>, which used to land on
/// <c>skill_list</c>, now goes to <c>experience_add</c>); shortlist 75%, 100%, 75%; the four read
/// clusters held 100% in all eight runs to date.</para>
///
/// <para><b>Floor policy, learned the hard way twice:</b> take at least THREE runs and floor at
/// the minimum observed minus headroom. Two agreeing runs misled this eval twice — pass 1 looked
/// like a clean 0.846 until a third run came in at 0.821, and pass 2 looked like 0.846/0.872 until
/// a third came in at 0.821 with a prompt that made NO tool call at all. Run-to-run variance on
/// this model is worth ~2 prompts, which is larger than either pass's aggregate gain, so the
/// aggregate floor cannot detect losing that gain — only the four read clusters, steady at 100%
/// across every run, are a gate worth its name. Do not tighten a floor to express a hope.</para>
///
/// <para>The five misses that survived both description passes were one class — a REQUIRED
/// argument the prompt cannot supply, so the model legitimately read first
/// (<c>employee_update</c> was a full replace needing firstName/lastName; <c>skill_create</c>
/// needs a categoryId; <c>style_exemplar_search</c> needed achievementIds). Descriptions could
/// not move those; affordances did — P1T-136 and P1T-137, measured below. Never lower a floor to
/// make a red run pass.</para>
///
/// <para><b>After P1T-136 + P1T-137, under the Temperature = 0 pin (measured 2026-08-29,
/// <c>gemini-3.5-flash-lite</c>):</b> two clean runs — first-tool <b>0.974</b> (38/39) and
/// <b>1.000</b> (39/39), 0 errors in both. Every cluster read 100% in both runs except shortlist,
/// which read 75% then 100%; its single miss is <c>sl-jd-paste</c> landing on <c>skill_list</c>.
/// The two affordance fixes closed what descriptions could not: <c>style</c> went 0% → 100%
/// (P1T-136's Theme Mode) and <c>writes</c> 83% → 100% (P1T-137's Partial Update, plus
/// <c>skill_create</c>'s prerequisite reads now scored as correct). Aggregate moved 0.821–0.872
/// → 0.974–1.000.</para>
///
/// <para><b>Floors NOT tightened yet, deliberately.</b> The policy below wants three runs; the
/// day's free-tier quota (500 requests, 39 per run) ran out after two clean ones. Two agreeing
/// runs have misled this eval twice already — that is exactly the trap the policy exists for, so
/// the floors stay where they are until a third clean run lands. Tighten then, from the minimum
/// of three, not from the two above.</para>
///
/// <para><b>Reading a red run:</b> a run past the error ceiling is not a measurement. Two runs on
/// 2026-08-29 rendered as <c>writes 0%</c> / total collapse purely because the transport died
/// partway through on quota. The report now says so at the top, and a fault carries its HTTP
/// status, so a 429 is legible as a 429. Check that before believing a cluster fell.</para>
///
/// <para><b>P1T-138 (temperature pin, re-baseline pending):</b> <c>ToolSelectionRunner</c> now
/// pins <c>Temperature = 0</c> to cut the run-to-run variance described above (seed is not settable
/// — the Gemini OpenAI-compat endpoint 400s on an unrecognized "seed" field). The floors below are
/// still the pre-pin ones; re-measuring under Temperature = 0 needs at least three live runs
/// (`dotnet test tests/Mcp.Tests --filter "Category=eval"`) and was blocked on 2026-08-28 by the
/// `gemini-3.5-flash-lite` free-tier daily quota (`GenerateRequestsPerDayPerProjectPerModel-FreeTier`,
/// 500 requests/day) being exhausted from the day's other eval passes — every prompt in the first
/// attempted run 429'd. Re-run once the quota resets and tighten these floors only from measured
/// data.</para>
/// </summary>
public static class ToolSelectionBaselines
{
    public const double FirstToolAccuracyFloor = 0.79;  // min observed 0.821 over 8 runs, minus headroom
    public const double AnyCallAccuracyFloor = 0.79;    // tracks first-tool on this set
    public const int ErrorCeiling = 2;                  // measured 0; headroom for transport flakes

    /// <summary>Per-cluster first-tool floors. The four read clusters at 1.0 are the real gate —
    /// 100% in every run measured, so any dip is signal. shortlist and writes sit at the lowest
    /// figure each has held (0.75), which is where variance put them, not where the pass left them
    /// (typically 100% and 83%): they catch a collapse, not a slip. style is absent on purpose
    /// (0% throughout — an affordance gap, P1T-136).</summary>
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

        // Past the error ceiling the figures below are an artifact of transport, not of selection —
        // a quota-exhausted run reads as a total collapse of whichever clusters ran last. Say so at
        // the top, where someone skimming the cluster table will see it (P1T-137).
        if (a.Errors > ToolSelectionBaselines.ErrorCeiling)
        {
            sb.AppendLine(
                $"> **Not a usable measurement.** {a.Errors} of {a.Results.Count} prompts failed in " +
                "transport, so every figure below understates selection. Check the per-prompt errors " +
                "for the status code (a 429 means quota, not a regression) and re-run.");
            sb.AppendLine();
        }
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
