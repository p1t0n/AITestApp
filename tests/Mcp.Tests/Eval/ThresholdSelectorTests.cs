using EmployeeManager.RetrievalEval;
using FluentAssertions;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the P1T-45 threshold selection rule: keep negative-FP-rate within the bound,
/// then maximise recall@5, then break ties on MRR; if no threshold honours the FP bound, fall
/// back to the least-leaky one.
/// </summary>
public class ThresholdSelectorTests
{
    [Fact]
    public void Picks_the_highest_recall_among_thresholds_within_the_fp_bound()
    {
        var results = new[]
        {
            At(0.20, recall: 0.95, mrr: 0.90, fpRate: 0.25), // best recall but leaks too much
            At(0.30, recall: 0.90, mrr: 0.85, fpRate: 0.10), // within bound (inclusive), best recall
            At(0.40, recall: 0.80, mrr: 0.95, fpRate: 0.00),
        };

        ThresholdSelector.SelectWinner(results).Threshold.Should().Be(0.30);
    }

    [Fact]
    public void Breaks_recall_ties_on_mrr()
    {
        var results = new[]
        {
            At(0.30, recall: 0.85, mrr: 0.70, fpRate: 0.05),
            At(0.35, recall: 0.85, mrr: 0.80, fpRate: 0.05), // same recall, better MRR
        };

        ThresholdSelector.SelectWinner(results).Threshold.Should().Be(0.35);
    }

    [Fact]
    public void Falls_back_to_the_minimum_fp_rate_when_nothing_honours_the_bound()
    {
        var results = new[]
        {
            At(0.20, recall: 0.95, mrr: 0.90, fpRate: 0.40),
            At(0.30, recall: 0.70, mrr: 0.60, fpRate: 0.20), // least leaky wins despite worse recall
            At(0.25, recall: 0.90, mrr: 0.80, fpRate: 0.30),
        };

        ThresholdSelector.SelectWinner(results).Threshold.Should().Be(0.30);
    }

    [Fact]
    public void Fallback_ties_on_fp_rate_are_broken_by_recall_then_mrr()
    {
        var results = new[]
        {
            At(0.20, recall: 0.80, mrr: 0.90, fpRate: 0.20),
            At(0.25, recall: 0.90, mrr: 0.60, fpRate: 0.20), // same FP, better recall
        };

        ThresholdSelector.SelectWinner(results).Threshold.Should().Be(0.25);
    }

    [Fact]
    public void Full_ties_resolve_to_the_earliest_candidate_for_determinism()
    {
        var results = new[]
        {
            At(0.30, recall: 0.85, mrr: 0.80, fpRate: 0.05),
            At(0.35, recall: 0.85, mrr: 0.80, fpRate: 0.05),
        };

        ThresholdSelector.SelectWinner(results).Threshold.Should().Be(0.30);
    }

    [Fact]
    public void Refuses_an_empty_candidate_list()
    {
        var act = () => ThresholdSelector.SelectWinner([]);

        act.Should().Throw<ArgumentException>();
    }

    private static ThresholdResult At(double threshold, double recall, double mrr, double fpRate)
        => new(threshold, new EvalMetrics(recall, mrr, fpRate), KeywordRecallAt5: recall);
}
