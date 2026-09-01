using ExpertToJob.RetrievalEval;
using FluentAssertions;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Proves the sweep's core efficiency trick is sound: results captured once at the sweep floor and
/// re-thresholded in memory must equal what a fresh search at each threshold would return. The
/// "fresh run" here is a hand-rolled model of the search semantics (similarity floor, best-first,
/// top-5) over fake similarities — no embeddings, fully deterministic.
/// </summary>
public class ThresholdReRankerTests
{
    private const int TopK = 5;

    /// <summary>Per-employee best similarity for one fake query, deliberately straddling every
    /// sweep threshold and exceeding top-5 at the floor.</summary>
    private static readonly IReadOnlyDictionary<string, double> Similarities =
        new Dictionary<string, double>
        {
            ["ada"] = 0.62,
            ["ben"] = 0.505,
            ["cyd"] = 0.50,
            ["dee"] = 0.35,
            ["eli"] = 0.30,
            ["fay"] = 0.22,
            ["gus"] = 0.16,
        };

    [Fact]
    public void Rethresholding_the_floor_capture_matches_a_fresh_run_at_every_swept_threshold()
    {
        var cached = FreshRun(0.15);

        foreach (var threshold in SweepRange.Parse("0.15:0.50:0.05"))
        {
            ThresholdReRanker.Apply(cached, threshold)
                .Should().Equal(
                    FreshRun(threshold).Select(h => h.Key),
                    because: $"re-ranking the cached floor capture at {threshold} must equal a fresh run");
        }
    }

    [Fact]
    public void A_threshold_above_every_similarity_returns_no_one()
        => ThresholdReRanker.Apply(FreshRun(0.15), 0.9).Should().BeEmpty();

    [Fact]
    public void Evaluates_metrics_and_the_keyword_subset_at_a_given_threshold()
    {
        // Two positives (one keyword, one paraphrase) and one negative, with hand-checkable hits.
        var cached = new[]
        {
            new CachedQueryResult(
                new GoldenQuery("kw", GoldenQueryCategory.Keyword, ["ada", "ben"]),
                [new ScoredHit("ada", 0.60), new ScoredHit("ben", 0.20)]),
            new CachedQueryResult(
                new GoldenQuery("para", GoldenQueryCategory.Paraphrase, ["cyd"]),
                [new ScoredHit("cyd", 0.55)]),
            new CachedQueryResult(
                new GoldenQuery("neg", GoldenQueryCategory.Negative, []),
                [new ScoredHit("gus", 0.31)]),
        };

        var at30 = SweepEvaluator.EvaluateAt(cached, 0.30);

        // At 0.30 'ben' (0.20) is dropped: kw recall 1/2, para recall 1; the negative still leaks.
        at30.Threshold.Should().Be(0.30);
        at30.Metrics.RecallAt5.Should().BeApproximately(0.75, 1e-9);
        at30.Metrics.MeanReciprocalRank.Should().Be(1.0);
        at30.Metrics.NegativeFalsePositiveRate.Should().Be(1.0);
        at30.KeywordRecallAt5.Should().BeApproximately(0.5, 1e-9);

        var at40 = SweepEvaluator.EvaluateAt(cached, 0.40);

        // At 0.40 the negative's leak (0.31) is silenced too.
        at40.Metrics.NegativeFalsePositiveRate.Should().Be(0.0);
        at40.KeywordRecallAt5.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Sweep_evaluates_every_threshold_in_order()
    {
        var cached = new[]
        {
            new CachedQueryResult(
                new GoldenQuery("kw", GoldenQueryCategory.Keyword, ["ada"]),
                [new ScoredHit("ada", 0.45)]),
        };

        var results = SweepEvaluator.Sweep(cached, [0.30, 0.40, 0.50]);

        results.Select(r => r.Threshold).Should().Equal(0.30, 0.40, 0.50);
        results.Select(r => r.Metrics.RecallAt5).Should().Equal(1.0, 1.0, 0.0);
    }

    /// <summary>Model of the production search at one threshold: drop below-floor hits, best first,
    /// top-5 — the semantics <c>SemanticSearchService</c> applies in SQL.</summary>
    private static IReadOnlyList<ScoredHit> FreshRun(double minSimilarity)
        => Similarities
            .Where(kv => kv.Value >= minSimilarity)
            .OrderByDescending(kv => kv.Value)
            .Take(TopK)
            .Select(kv => new ScoredHit(kv.Key, kv.Value))
            .ToList();
}
