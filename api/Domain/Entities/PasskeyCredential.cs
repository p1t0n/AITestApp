namespace CvManager.Domain.Entities;

/// <summary>
/// A WebAuthn/FIDO2 credential enrolled by a <see cref="User"/>. Field shapes follow what a
/// server-side WebAuthn library (e.g. fido2-net-lib) stores and replays during assertion.
/// </summary>
public class PasskeyCredential
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Raw WebAuthn credential id returned by the authenticator. Globally unique.</summary>
    public byte[] CredentialId { get; set; } = [];

    /// <summary>COSE-encoded public key used to verify assertions.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>Signature counter; advanced on each assertion to detect cloned authenticators.</summary>
    public long SignatureCounter { get; set; }

    /// <summary>Authenticator AAGUID (model identifier), if reported.</summary>
    public Guid? AaGuid { get; set; }

    /// <summary>Reported transports (e.g. "internal,hybrid"), stored as given by the client.</summary>
    public string? Transports { get; set; }

    /// <summary>User-friendly device label, e.g. "MacBook Touch ID".</summary>
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
