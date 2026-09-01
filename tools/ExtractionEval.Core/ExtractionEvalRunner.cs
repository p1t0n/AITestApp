using System.Text;
using ExpertToJob.Agents.Agents;

namespace ExpertToJob.ExtractionEval;

/// <summary>Runs the extractor over the golden set with free-tier-friendly pacing and scores
/// each JD. Shared by the CLI and the live regression gate.</summary>
public static class ExtractionEvalRunner
{
    public static async Task<EvalAggregate> RunAsync(
        IJdRequirementExtractor extractor,
        IReadOnlyList<GoldenJd> goldenSet,
        TimeSpan pacing,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var scores = new List<JdScore>();
        foreach (var golden in goldenSet)
        {
            var outcome = await extractor.ExtractAsync(golden.JobDescription, ct);
            var score = ExtractionScoring.Score(golden, outcome);
            scores.Add(score);
            progress?.Invoke(Describe(score));

            if (!ReferenceEquals(golden, goldenSet[^1]))
            {
                await Task.Delay(pacing, ct);
            }
        }

        return EvalAggregate.From(scores);
    }

    public static string Describe(JdScore s)
    {
        if (s.Fault is { } fault)
        {
            return $"{s.Id,-28} FAULT: {fault}";
        }

        var line =
            $"{s.Id,-28} n={s.RequirementCount} recall={s.ConceptRecall:P0} mustHaveP={s.MustHavePrecision:P0} " +
            $"verbatim={s.EvidenceVerbatimRate:P0} sen={(s.SeniorityCorrect ? "ok" : "MISS")} " +
            $"loc={(s.LocationCorrect ? "ok" : "MISS")} fabr={s.Fabrications.Count}";
        return s.Fabrications.Count > 0 ? line + "  | " + string.Join(" | ", s.Fabrications) : line;
    }

    /// <summary>Markdown report for the CLI (and for pasting into the manual).</summary>
    public static string RenderReport(EvalAggregate aggregate, string model, DateOnly date)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extraction-fidelity eval report");
        sb.AppendLine();
        sb.AppendLine($"- Date: {date:yyyy-MM-dd}; model: `{model}`; golden JDs: {aggregate.Scores.Count}");
        sb.AppendLine(
            $"- Concept recall **{aggregate.ConceptRecall:F3}** (floor {ExtractionEvalBaselines.ConceptRecallFloor:F2}) · " +
            $"must-have precision **{aggregate.MustHavePrecision:F3}** (floor {ExtractionEvalBaselines.MustHavePrecisionFloor:F2}) · " +
            $"evidence verbatim **{aggregate.EvidenceVerbatimRate:F3}** (floor {ExtractionEvalBaselines.EvidenceVerbatimFloor:F2})");
        sb.AppendLine(
            $"- Seniority accuracy **{aggregate.SeniorityAccuracy:F3}** · location accuracy **{aggregate.LocationAccuracy:F3}** · " +
            $"fabrications **{aggregate.FabricationCount}** (hard ceiling {ExtractionEvalBaselines.FabricationCeiling}) · " +
            $"faults **{aggregate.FaultCount}**");
        sb.AppendLine();
        sb.AppendLine("| JD | n | recall | mustHaveP | verbatim | seniority | location | fabrications |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | :---: | :---: | --- |");
        foreach (var s in aggregate.Scores)
        {
            if (s.Fault is { } fault)
            {
                sb.AppendLine($"| {s.Id} | - | - | - | - | - | - | FAULT: {fault} |");
                continue;
            }

            sb.AppendLine(
                $"| {s.Id} | {s.RequirementCount} | {s.ConceptRecall:P0} | {s.MustHavePrecision:P0} | " +
                $"{s.EvidenceVerbatimRate:P0} | {(s.SeniorityCorrect ? "ok" : "miss")} | " +
                $"{(s.LocationCorrect ? "ok" : "miss")} | {string.Join("; ", s.Fabrications)} |");
        }

        return sb.ToString();
    }

    /// <summary>The gate the live regression test and the CLI exit code share.</summary>
    public static IReadOnlyList<string> GateViolations(EvalAggregate a)
    {
        var violations = new List<string>();
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                violations.Add(message);
            }
        }

        Check(a.FabricationCount <= ExtractionEvalBaselines.FabricationCeiling,
            $"fabrications {a.FabricationCount} > ceiling {ExtractionEvalBaselines.FabricationCeiling}");
        Check(a.FaultCount <= ExtractionEvalBaselines.FaultCeiling,
            $"faults {a.FaultCount} > ceiling {ExtractionEvalBaselines.FaultCeiling}");
        Check(a.ConceptRecall >= ExtractionEvalBaselines.ConceptRecallFloor,
            $"concept recall {a.ConceptRecall:F3} < floor {ExtractionEvalBaselines.ConceptRecallFloor}");
        Check(a.MustHavePrecision >= ExtractionEvalBaselines.MustHavePrecisionFloor,
            $"must-have precision {a.MustHavePrecision:F3} < floor {ExtractionEvalBaselines.MustHavePrecisionFloor}");
        Check(a.EvidenceVerbatimRate >= ExtractionEvalBaselines.EvidenceVerbatimFloor,
            $"evidence verbatim {a.EvidenceVerbatimRate:F3} < floor {ExtractionEvalBaselines.EvidenceVerbatimFloor}");
        Check(a.SeniorityAccuracy >= ExtractionEvalBaselines.SeniorityAccuracyFloor,
            $"seniority accuracy {a.SeniorityAccuracy:F3} < floor {ExtractionEvalBaselines.SeniorityAccuracyFloor}");
        Check(a.LocationAccuracy >= ExtractionEvalBaselines.LocationAccuracyFloor,
            $"location accuracy {a.LocationAccuracy:F3} < floor {ExtractionEvalBaselines.LocationAccuracyFloor}");
        return violations;
    }
}
