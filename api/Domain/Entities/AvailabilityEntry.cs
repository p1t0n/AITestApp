namespace ExpertToJob.Domain.Entities;

/// <summary>
/// One step in an expert's availability step-function. The entry's <see cref="CapacityPercent"/>
/// holds from <see cref="EffectiveFrom"/> until the next entry (by date) overrides it.
/// Capacity at a target date = entry with the greatest EffectiveFrom &lt;= date.
/// </summary>
public class AvailabilityEntry
{
    public Guid Id { get; set; }

    public Guid ExpertId { get; set; }
    public Expert Expert { get; set; } = null!;

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Percent of full-time the expert is AVAILABLE (0-100).</summary>
    public int CapacityPercent { get; set; }
}
