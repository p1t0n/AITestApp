namespace ExpertToJob.RetrievalEval;

/// <summary>One returned expert with the similarity the search scored it at.</summary>
public sealed record ScoredHit(string Key, double Similarity);

/// <summary>
/// One golden query's scored results captured once at the sweep's floor threshold. The similarities
/// make the capture reusable: every higher threshold is a pure in-memory filter over these hits.
/// </summary>
public sealed record CachedQueryResult(GoldenQuery Query, IReadOnlyList<ScoredHit> Hits);

/// <summary>
/// Post-hoc thresholding over a floor capture. Sound because raising the similarity floor only ever
/// removes the worst-ranked hits: the top-K at any threshold above the floor is exactly the floor's
/// top-K with the below-threshold tail dropped, so nothing needs re-embedding or re-querying.
/// </summary>
public static class ThresholdReRanker
{
    /// <summary>The keys a fresh search at <paramref name="minSimilarity"/> would return.</summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<ScoredHit> hits, double minSimilarity)
        => hits
            .Where(h => h.Similarity >= minSimilarity)
            .Select(h => h.Key)
            .ToList();
}

/// <summary>Everything one threshold scored: the standard metrics plus the keyword-subset recall
/// the hybrid-search decision (P1T-45) consumes.</summary>
public sealed record ThresholdResult(double Threshold, EvalMetrics Metrics, double KeywordRecallAt5);

/// <summary>Scores a floor capture at each candidate threshold — the pure heart of the sweep.</summary>
public static class SweepEvaluator
{
    public static IReadOnlyList<ThresholdResult> Sweep(
        IReadOnlyList<CachedQueryResult> cached, IEnumerable<double> thresholds)
        => thresholds.Select(t => EvaluateAt(cached, t)).ToList();

    public static ThresholdResult EvaluateAt(IReadOnlyList<CachedQueryResult> cached, double threshold)
    {
        var outcomes = cached
            .Select(c => (c.Query.Category, Outcome: new QueryOutcome(
                IsNegative: c.Query.Category == GoldenQueryCategory.Negative,
                Expected: c.Query.Expected.ToHashSet(),
                Returned: ThresholdReRanker.Apply(c.Hits, threshold))))
            .ToList();

        var keywordOnly = outcomes
            .Where(o => o.Category == GoldenQueryCategory.Keyword)
            .Select(o => o.Outcome)
            .ToList();

        return new ThresholdResult(
            threshold,
            RetrievalMetrics.Compute(outcomes.Select(o => o.Outcome).ToList()),
            RetrievalMetrics.Compute(keywordOnly).RecallAt5);
    }
}
