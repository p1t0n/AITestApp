using EmployeeManager.Domain.Entities;

namespace EmployeeManager.Application.Availability;

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
}
