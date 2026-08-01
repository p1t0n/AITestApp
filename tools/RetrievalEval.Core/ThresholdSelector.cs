namespace CvManager.RetrievalEval;

/// <summary>
/// The P1T-45 threshold selection rule. Precision comes first as a hard constraint — a threshold
/// that lets negative queries hallucinate matches is disqualified — then recall@5 is maximised and
/// MRR breaks ties. When every candidate leaks past the bound, the least-leaky one wins (same
/// tie-breaks), so the sweep always names a winner. Full ties resolve to the earliest candidate,
/// keeping the choice deterministic.
/// </summary>
public static class ThresholdSelector
{
    /// <summary>The FP bound: at most this fraction of negative queries may return anyone.</summary>
    public const double MaxNegativeFalsePositiveRate = 0.10;

    public static ThresholdResult SelectWinner(
        IReadOnlyList<ThresholdResult> candidates,
        double maxNegativeFalsePositiveRate = MaxNegativeFalsePositiveRate)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("Cannot select a winner from an empty sweep.", nameof(candidates));
        }

        var withinBound = candidates
            .Where(c => c.Metrics.NegativeFalsePositiveRate <= maxNegativeFalsePositiveRate)
            .ToList();

        return withinBound.Count > 0
            ? BestByRecallThenMrr(withinBound)
            : BestByRecallThenMrr(MinimumFalsePositives(candidates));

        // OrderBy is stable, so among full ties the earliest candidate wins.
        static ThresholdResult BestByRecallThenMrr(IReadOnlyList<ThresholdResult> group)
            => group
                .OrderByDescending(c => c.Metrics.RecallAt5)
                .ThenByDescending(c => c.Metrics.MeanReciprocalRank)
                .First();

        static IReadOnlyList<ThresholdResult> MinimumFalsePositives(IReadOnlyList<ThresholdResult> group)
        {
            var floor = group.Min(c => c.Metrics.NegativeFalsePositiveRate);
            return group.Where(c => c.Metrics.NegativeFalsePositiveRate == floor).ToList();
        }
    }
}
