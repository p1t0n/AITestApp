using FluentAssertions;

using ExpertToJob.RetrievalEval;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the pure retrieval-metric math. Every expected value is hand-computable from the
/// listed outcomes, so a regression here means the formula changed, not the data.
/// </summary>
public class RetrievalMetricsTests
{
    [Fact]
    public void Recall_at_5_is_the_expected_fraction_found_in_the_top_five()
    {
        // 2 of 2 expected in top 5 -> 1.0; 1 of 2 -> 0.5. Mean = 0.75.
        var outcomes = new[]
        {
            Positive(expected: ["a", "b"], returned: ["a", "b", "x"]),
            Positive(expected: ["c", "d"], returned: ["c", "x", "y", "z", "w"]),
        };

        RetrievalMetrics.Compute(outcomes).RecallAt5.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void Recall_at_5_ignores_hits_beyond_rank_five()
    {
        // "a" sits at rank 6 — outside the top 5 window, so recall is 0.
        var outcomes = new[]
        {
            Positive(expected: ["a"], returned: ["u", "v", "w", "x", "y", "a"]),
        };

        RetrievalMetrics.Compute(outcomes).RecallAt5.Should().Be(0);
    }

    [Fact]
    public void Mrr_is_the_mean_reciprocal_rank_of_the_first_expected_hit()
    {
        // First hits at rank 1 (1.0) and rank 3 (1/3). Mean = 2/3.
        var outcomes = new[]
        {
            Positive(expected: ["a"], returned: ["a", "x"]),
            Positive(expected: ["b", "c"], returned: ["x", "y", "c"]),
        };

        RetrievalMetrics.Compute(outcomes).MeanReciprocalRank.Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Mrr_counts_a_miss_as_zero()
    {
        // Rank-1 hit (1.0) averaged with a total miss (0.0) = 0.5.
        var outcomes = new[]
        {
            Positive(expected: ["a"], returned: ["a"]),
            Positive(expected: ["b"], returned: ["x", "y"]),
        };

        RetrievalMetrics.Compute(outcomes).MeanReciprocalRank.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Negative_fp_rate_is_the_fraction_of_negative_queries_returning_anything()
    {
        var outcomes = new[]
        {
            Negative(returned: []),
            Negative(returned: ["a"]), // false positive
            Negative(returned: []),
            Negative(returned: []),
        };

        RetrievalMetrics.Compute(outcomes).NegativeFalsePositiveRate.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Negative_queries_do_not_influence_recall_or_mrr_and_vice_versa()
    {
        var outcomes = new[]
        {
            Positive(expected: ["a"], returned: ["a"]),
            Negative(returned: ["junk"]),
        };

        var metrics = RetrievalMetrics.Compute(outcomes);

        metrics.RecallAt5.Should().Be(1.0);
        metrics.MeanReciprocalRank.Should().Be(1.0);
        metrics.NegativeFalsePositiveRate.Should().Be(1.0);
    }

    [Fact]
    public void Empty_groups_yield_zero_rather_than_dividing_by_zero()
    {
        var onlyNegative = RetrievalMetrics.Compute([Negative(returned: [])]);
        onlyNegative.RecallAt5.Should().Be(0);
        onlyNegative.MeanReciprocalRank.Should().Be(0);

        var onlyPositive = RetrievalMetrics.Compute([Positive(expected: ["a"], returned: ["a"])]);
        onlyPositive.NegativeFalsePositiveRate.Should().Be(0);
    }

    private static QueryOutcome Positive(string[] expected, string[] returned)
        => new(IsNegative: false, expected.ToHashSet(), returned);

    private static QueryOutcome Negative(string[] returned)
        => new(IsNegative: true, new HashSet<string>(), returned);
}
