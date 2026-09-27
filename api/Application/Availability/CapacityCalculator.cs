using System.Linq.Expressions;
using ExpertToJob.Domain.Entities;

namespace ExpertToJob.Application.Availability;

/// <summary>
/// Pure availability step-function logic. Capacity at a target date is the
/// <see cref="AvailabilityEntry.CapacityPercent"/> of the entry with the greatest
/// <see cref="AvailabilityEntry.EffectiveFrom"/> that is on or before the date.
/// Before the first entry, capacity is 0 (unknown / not yet on the bench).
/// </summary>
public static class CapacityCalculator
{
    /// <summary>Capacity percent (0-100) effective on <paramref name="onDate"/>.</summary>
    public static int CapacityOn(IEnumerable<AvailabilityEntry> entries, DateOnly onDate)
    {
        AvailabilityEntry? best = null;
        foreach (var e in entries)
        {
            if (e.EffectiveFrom <= onDate && (best is null || e.EffectiveFrom > best.EffectiveFrom))
                best = e;
        }
        return best?.CapacityPercent ?? 0;
    }

    /// <summary>
    /// The same step function written over an <see cref="Expert"/>, so a caller can order and page
    /// on availability <em>in SQL</em> (EXP-45) instead of sorting a list it already paid to
    /// materialise. EF turns it into a correlated subquery; the coalesce is the "before the first
    /// entry, capacity is 0" rule, which a bare <c>FirstOrDefault</c> would express as an empty row
    /// rather than a zero.
    ///
    /// <para>It is a second spelling of the rule above, and that is the one thing worth guarding:
    /// <c>CapacityCalculatorTests.The_two_spellings_of_the_step_function_agree</c> runs the two
    /// against the same schedules and fails if they ever disagree.</para>
    /// </summary>
    public static Expression<Func<Expert, int>> CapacityOn(DateOnly onDate) => e =>
        e.AvailabilityEntries
            .Where(a => a.EffectiveFrom <= onDate)
            .OrderByDescending(a => a.EffectiveFrom)
            .Select(a => (int?)a.CapacityPercent)
            .FirstOrDefault() ?? 0;

    /// <summary>
    /// "Is this person's availability today between these two numbers?", as SQL — the predicate
    /// behind the roster's availability bands and behind their counts (EXP-47).
    ///
    /// <para>Assembled from <see cref="CapacityOn(DateOnly)"/>'s own expression tree rather than
    /// written out again. There are already two spellings of the step function in this file and a
    /// test whose only job is to keep them honest; a third, differing only in the comparison
    /// bolted to the end, would be the one that drifts. Here the subquery <em>is</em> the same
    /// node — comparing it against a bound is all this method contributes.</para>
    /// </summary>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Inclusive upper bound.</param>
    public static Expression<Func<Expert, bool>> CapacityBetween(DateOnly onDate, int min, int max)
    {
        var capacity = CapacityOn(onDate);
        var within = Expression.AndAlso(
            Expression.GreaterThanOrEqual(capacity.Body, Expression.Constant(min)),
            Expression.LessThanOrEqual(capacity.Body, Expression.Constant(max)));
        return Expression.Lambda<Func<Expert, bool>>(within, capacity.Parameters[0]);
    }
}
