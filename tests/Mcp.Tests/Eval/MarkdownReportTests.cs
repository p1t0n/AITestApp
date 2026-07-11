using EmployeeManager.RetrievalEval;
using FluentAssertions;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the sweep report renderer: one markdown table (threshold | recall@5 | MRR |
/// negative-FP-rate | keyword recall@5), run metadata up front, and the selected threshold called
/// out. Assertions are on exact lines so the committed baseline file format stays stable.
/// </summary>
public class MarkdownReportTests
{
    private static readonly ReportMetadata Metadata = new(
        ModelId: "text-embedding-3-small",
        CorpusSize: 24,
        QueryCount: 39,
        Date: "2026-07-11");

    private static readonly ThresholdResult[] Results =
    [
        new(0.30, new EvalMetrics(0.9125, 0.8542, 0.0), KeywordRecallAt5: 1.0),
        new(0.35, new EvalMetrics(0.85, 0.8542, 0.0), KeywordRecallAt5: 0.875),
    ];

    [Fact]
    public void Renders_the_metric_table_with_one_row_per_threshold()
    {
        var report = MarkdownReport.Render(Metadata, Results, selectedThreshold: null);
        var lines = report.Split('\n').Select(l => l.TrimEnd()).ToList();

        lines.Should().Contain(
            "| Threshold | Recall@5 | MRR | Negative FP rate | Keyword recall@5 |");
        lines.Should().Contain("| 0.300 | 0.9125 | 0.8542 | 0.0000 | 1.0000 |");
        lines.Should().Contain("| 0.350 | 0.8500 | 0.8542 | 0.0000 | 0.8750 |");
    }

    [Fact]
    public void Renders_the_run_metadata()
    {
        var report = MarkdownReport.Render(Metadata, Results, selectedThreshold: null);

        report.Should().Contain("text-embedding-3-small");
        report.Should().Contain("24");
        report.Should().Contain("39");
        report.Should().Contain("2026-07-11");
    }

    [Fact]
    public void Calls_out_the_selected_threshold_when_one_was_chosen()
    {
        var report = MarkdownReport.Render(Metadata, Results, selectedThreshold: 0.30);

        report.Should().Contain("**Selected threshold: 0.300**");
    }

    [Fact]
    public void Omits_the_selection_line_when_no_winner_was_computed()
        => MarkdownReport.Render(Metadata, Results, selectedThreshold: null)
            .Should().NotContain("Selected threshold");
}
