using System.ComponentModel.DataAnnotations;

namespace ExpertToJob.Agents.Usage;

/// <summary>
/// System-default token caps, inherited by any user whose per-user cap is null. Bound from the
/// "Usage" configuration section. Windows reset on UTC calendar boundaries (day, ISO-ish week
/// starting Monday, calendar month).
///
/// <para>No property defaults since EXP-107, which reverses EXP-89's code-default-only choice for
/// these three. They are knobs an operator genuinely has to turn without a deploy — the 50k daily
/// cap was hit inside a single verification session — and a value hidden in a code default is one
/// nobody knows they can change. So the shipped numbers live once, in
/// <c>api/Agents/appsettings.json</c>; a property default here would be the second copy EXP-89's
/// one-side rule forbids. The <see cref="RangeAttribute"/>s are enforced with
/// <c>ValidateOnStart</c>, so a missing or zero cap refuses to boot and names the key, rather than
/// starting a host that caps everyone at nothing.</para>
/// </summary>
public sealed class UsageOptions
{
    public const string Section = "Usage";

    /// <summary>Raised from 25k to 50k for the staffing pipeline (P1T-75): one run spends
    /// shortlist + N match + narrative tokens, and the old default left no headroom for real
    /// use.</summary>
    [Range(1, long.MaxValue, ErrorMessage = $"{Section}:{nameof(DefaultDailyTokens)} must be greater than 0.")]
    public long DefaultDailyTokens { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = $"{Section}:{nameof(DefaultWeeklyTokens)} must be greater than 0.")]
    public long DefaultWeeklyTokens { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = $"{Section}:{nameof(DefaultMonthlyTokens)} must be greater than 0.")]
    public long DefaultMonthlyTokens { get; set; }
}
