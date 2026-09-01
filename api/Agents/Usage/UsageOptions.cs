namespace ExpertToJob.Agents.Usage;

/// <summary>
/// System-default token caps, inherited by any user whose per-user cap is null. Bound from the
/// "Usage" configuration section. Windows reset on UTC calendar boundaries (day, ISO-ish week
/// starting Monday, calendar month).
/// </summary>
public sealed class UsageOptions
{
    public const string Section = "Usage";

    public long DefaultDailyTokens { get; set; } = 50000;
    public long DefaultWeeklyTokens { get; set; } = 150000;
    public long DefaultMonthlyTokens { get; set; } = 500000;
}
