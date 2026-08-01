namespace CvManager.Domain.Entities;

/// <summary>
/// One step in an employee's availability step-function. The entry's <see cref="CapacityPercent"/>
/// holds from <see cref="EffectiveFrom"/> until the next entry (by date) overrides it.
/// Capacity at a target date = entry with the greatest EffectiveFrom &lt;= date.
/// </summary>
public class AvailabilityEntry
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Percent of full-time the employee is AVAILABLE (0-100).</summary>
    public int CapacityPercent { get; set; }
}
