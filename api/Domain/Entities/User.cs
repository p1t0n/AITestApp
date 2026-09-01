using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// An authenticated account. Auth is passwordless: the only login credential is a
/// passkey (see <see cref="PasskeyCredential"/>). The <see cref="ControlWordHash"/> is
/// the sole account-recovery secret — used to register a new passkey after device loss.
/// <see cref="Role"/> splits staff (<c>ServiceManager</c>) from the people the CVs are about
/// (<c>Expert</c>); <see cref="TokenVersion"/> is how a live session is revoked.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Unique login identifier. Not verified — no email is ever sent.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the mandatory control word (set at signup). Verified during recovery to
    /// authorise registering a new passkey. Stored hashed, never in plaintext.
    /// </summary>
    public string ControlWordHash { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>
    /// What the account may reach. Defaults to <see cref="UserRole.Expert"/> because signup is
    /// open self-serve — staff are made deliberately (bootstrap config, or promotion), never by
    /// signing up. Existing accounts were migrated to <see cref="UserRole.ServiceManager"/>: they
    /// were all staff before the split existed.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Expert;

    /// <summary>
    /// Session generation. Minted into every token and re-checked against this column on every
    /// request, so bumping it refuses every token already issued for this account. Without it a
    /// deactivated or erased person keeps working until their token expires — which is why erasure
    /// depends on this field rather than on a short lifetime.
    /// </summary>
    public int TokenVersion { get; set; } = 1;

    /// <summary>
    /// The transparency-notice version this account acknowledged, most recently (P1T-183).
    /// Acknowledging is required to register, so a self-serve account always has one; the
    /// pre-existing staff accounts and the bootstrap invite carry null, which is honest — nobody
    /// showed them anything.
    ///
    /// <para>Kept on the account rather than only on <see cref="ProcessingRecord"/> because a person
    /// acknowledges before any roster row is theirs: signup creates no <see cref="Expert"/>, and the
    /// row they will eventually own may not exist yet. The record is written when the two meet.</para>
    /// </summary>
    public string? AcknowledgedNoticeVersion { get; set; }

    /// <summary>When <see cref="AcknowledgedNoticeVersion"/> was acknowledged. Null with it.</summary>
    public DateTimeOffset? NoticeAcknowledgedAt { get; set; }

    /// <summary>Per-user daily token cap. Null = inherit the system default from config.</summary>
    public long? DailyTokenCap { get; set; }

    /// <summary>Per-user weekly token cap. Null = inherit the system default from config.</summary>
    public long? WeeklyTokenCap { get; set; }

    /// <summary>Per-user monthly token cap. Null = inherit the system default from config.</summary>
    public long? MonthlyTokenCap { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Registered passkeys. A user may enrol more than one device.</summary>
    public ICollection<PasskeyCredential> Passkeys { get; set; } = new List<PasskeyCredential>();
}
