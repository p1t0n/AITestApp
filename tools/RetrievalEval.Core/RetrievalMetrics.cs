namespace CvManager.RetrievalEval;

/// <summary>
/// The outcome of running one golden query: what the search returned (employee keys, best first)
/// against what the golden set expected. Negative queries carry an empty expectation — for them a
/// non-empty result is a false positive.
/// </summary>
public sealed record QueryOutcome(
    bool IsNegative,
    IReadOnlySet<string> Expected,
    IReadOnlyList<string> Returned);

/// <summary>Aggregate retrieval-quality numbers for one eval run.</summary>
public sealed record EvalMetrics(
    /// <summary>Mean over positive queries of |expected ∩ top-5 returned| / |expected|.</summary>
    double RecallAt5,
    /// <summary>Mean over positive queries of 1/rank of the first expected hit (0 when none).</summary>
    double MeanReciprocalRank,
    /// <summary>Fraction of negative queries that returned anything at all.</summary>
    double NegativeFalsePositiveRate);

/// <summary>
/// Pure retrieval-metric math over per-query outcomes. No I/O, no embeddings — the deterministic
/// core of the eval harness, so it is unit-tested with hand-computable cases.
/// </summary>
public static class RetrievalMetrics
{
    private const int RecallWindow = 5;

    public static EvalMetrics Compute(IReadOnlyList<QueryOutcome> outcomes)
    {
        var positives = outcomes.Where(o => !o.IsNegative).ToList();
        var negatives = outcomes.Where(o => o.IsNegative).ToList();

        return new EvalMetrics(
            RecallAt5: Mean(positives, RecallAt5Of),
            MeanReciprocalRank: Mean(positives, ReciprocalRankOf),
            NegativeFalsePositiveRate: Mean(negatives, o => o.Returned.Count > 0 ? 1.0 : 0.0));
    }

    private static double RecallAt5Of(QueryOutcome outcome)
        => outcome.Returned.Take(RecallWindow).Count(outcome.Expected.Contains)
           / (double)outcome.Expected.Count;

    private static double ReciprocalRankOf(QueryOutcome outcome)
    {
        for (var rank = 1; rank <= outcome.Returned.Count; rank++)
        {
            if (outcome.Expected.Contains(outcome.Returned[rank - 1]))
            {
                return 1.0 / rank;
            }
        }

        return 0;
    }

    /// <summary>Mean of a per-outcome score; an empty group is 0 rather than NaN.</summary>
    private static double Mean(IReadOnlyList<QueryOutcome> group, Func<QueryOutcome, double> score)
        => group.Count == 0 ? 0 : group.Average(score);
}
