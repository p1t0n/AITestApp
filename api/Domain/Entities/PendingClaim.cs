using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// One person's attempt to be recognised as the subject of one roster row (P1T-184), and what a
/// Service Manager decided about it. Kept after resolution rather than deleted: a flag on the
/// <see cref="Expert"/> cannot express "rejected, then claimed again by somebody else", and this is
/// exactly the sort of decision that gets audited later.
///
/// <para>The whole record exists because <see cref="User.Email"/> is never verified and this service
/// sends no mail. A matching address is the only signal available and it proves nothing, so the
/// match creates a request rather than a binding — with one exception, <see cref="ClaimCode"/>,
/// which is proof because a Service Manager handed it over in person.</para>
/// </summary>
public class PendingClaim
{
    public Guid Id { get; set; }

    /// <summary>The account asking. Cascade-deleted with it — erasure takes the request too.</summary>
    public Guid ClaimantUserId { get; set; }

    public User? Claimant { get; set; }

    /// <summary>
    /// The address that matched, as it stood when the claim was raised. Kept alongside the FK rather
    /// than read through it: a Service Manager may change an account's email afterwards, and the
    /// approver's screen has to show what was actually matched on, not what it says today.
    /// </summary>
    public string ClaimantEmail { get; set; } = string.Empty;

    /// <summary>
    /// The row being claimed — null when the match was ambiguous. A null target is not a claim on
    /// anything: it is the raised flag itself (<see cref="ClaimState.Ambiguous"/>), because
    /// <c>Expert.Email</c> is unique only among Active rows and auto-picking between duplicates
    /// would hand one person another person's CV on a coin flip.
    /// </summary>
    public Guid? ExpertId { get; set; }

    public Expert? Expert { get; set; }

    /// <summary>How many non-Draft rows the address matched. 1 for an ordinary claim, ≥2 for a
    /// flag — the number the approver needs in order to know what they are looking at.</summary>
    public int MatchCount { get; set; }

    public ClaimState State { get; set; } = ClaimState.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The Service Manager who decided. Null while the claim is open.</summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}
