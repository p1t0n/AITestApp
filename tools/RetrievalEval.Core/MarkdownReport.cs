using System.Globalization;
using System.Text;

namespace EmployeeManager.RetrievalEval;

/// <summary>What identifies one eval run in the committed report. The date is passed in by the
/// caller (a CLI arg) rather than read from the clock, keeping rendering deterministic.</summary>
public sealed record ReportMetadata(
    string ModelId,
    int CorpusSize,
    int QueryCount,
    string Date);

/// <summary>
/// Renders a sweep into the markdown report format committed under <c>manuals/</c>: run metadata,
/// then one table row per threshold, then the winner the selection rule chose. Pure string
/// building — invariant culture, fixed decimal places — so the same numbers always render the
/// same bytes.
/// </summary>
public static class MarkdownReport
{
    public static string Render(
        ReportMetadata metadata,
        IReadOnlyList<ThresholdResult> results,
        double? selectedThreshold)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Retrieval eval sweep");
        sb.AppendLine();
        sb.AppendLine(Invariant($"- Embedding model: `{metadata.ModelId}`"));
        sb.AppendLine(Invariant($"- Corpus size: {metadata.CorpusSize} employees"));
        sb.AppendLine(Invariant($"- Golden set: {metadata.QueryCount} queries"));
        sb.AppendLine(Invariant($"- Date: {metadata.Date}"));
        sb.AppendLine();
        sb.AppendLine("| Threshold | Recall@5 | MRR | Negative FP rate | Keyword recall@5 |");
        sb.AppendLine("|-----------|----------|-----|------------------|------------------|");

        foreach (var r in results)
        {
            sb.AppendLine(Invariant(
                $"| {r.Threshold:F3} | {r.Metrics.RecallAt5:F4} | {r.Metrics.MeanReciprocalRank:F4} | {r.Metrics.NegativeFalsePositiveRate:F4} | {r.KeywordRecallAt5:F4} |"));
        }

        if (selectedThreshold is { } selected)
        {
            sb.AppendLine();
            sb.AppendLine(Invariant(
                $"**Selected threshold: {selected:F3}** (rule: negative-FP ≤ 10% → max recall@5 → max MRR)"));
        }

        return sb.ToString();
    }

    private static string Invariant(FormattableString text)
        => text.ToString(CultureInfo.InvariantCulture);
}
