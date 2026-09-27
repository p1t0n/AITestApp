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
}
