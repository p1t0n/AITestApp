using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// An authenticated account. Auth is passwordless: the only login credential is a
/// passkey (see <see cref="PasskeyCredential"/>). The <see cref="ControlWordHash"/> is
/// the sole account-recovery secret — used to register a new passkey after device loss.
/// Roles are flat (no admin); any signed-in user may manage any user.
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
