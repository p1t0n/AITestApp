using ExpertToJob.Application.Auth;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Auth configuration. Bound from the "Auth" section. The JWT settings are the contract shared
/// with the Agents service — both sign/validate session tokens with the same key/issuer/audience.
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public JwtOptions Jwt { get; set; } = new();
    public PasskeyOptions Passkey { get; set; } = new();

    /// <summary>
    /// The email of the first Administrator. At startup that account is created (as an invite
    /// awaiting its passkey) or promoted if it already exists — see
    /// <see cref="AdministratorBootstrapper"/>. Empty disables the bootstrap: signup is open but
    /// self-serve signups are Users, so without this a fresh database has no staff at all.
    ///
    /// <para>Renamed from <c>SeedServiceManagerEmail</c> (P1T-236). Configuration binding answers a
    /// key it does not know with an empty string rather than an error, so the old key is rejected at
    /// startup by <see cref="RetiredAuthKeys"/> instead of being silently ignored.</para>
    /// </summary>
    public string SeedAdministratorEmail { get; set; } = string.Empty;
}

/// <summary>
/// Symmetric (HS256) session-token settings. The signing key MUST match in the Web and Agents
/// configuration — Web issues tokens, both services validate them. Keep the production key in a
/// secret store, not source control.
///
/// <para>The <c>iss</c> and <c>aud</c> are deliberately absent: they are not an operator knob but
/// the name of this product's session, so both hosts mint and validate against
/// <see cref="SessionIdentity"/> directly (EXP-106). EXP-89 had already stopped them being shipped
/// twice in JSON and left them bindable with the shared constant as the default — and nothing ever
/// bound them, which left a settable property whose only effect would have been to let one host's
/// configuration move without the other's. That is exactly the shape of P1T-176.</para>
/// </summary>
public sealed class JwtOptions
{
    /// <summary>HS256 signing key. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Session lifetime. Short on purpose: <c>User.TokenVersion</c> is the revocation mechanism,
    /// and this is the window in which a *lifetime*-only failure (a token version check that never
    /// ran) would still let a revoked session through.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>WebAuthn relying-party settings for fido2-net-lib.</summary>
public sealed class PasskeyOptions
{
    /// <summary>Relying-party id — the registrable domain (no scheme/port), e.g. "localhost".
    ///
    /// <para><b>No code default, on purpose</b> (EXP-89). This and <see cref="Origins"/> are the
    /// half of the relying-party identity that genuinely differs per deployment, so they are
    /// configured and only configured; a shipped <c>"localhost"</c> fallback is how a production
    /// host comes to bind every passkey to a domain nobody chose, silently and irreversibly — a
    /// credential registered under one RP id is unusable under another.</para></summary>
    public string ServerDomain { get; set; } = string.Empty;

    /// <summary>Human-readable relying-party name shown in the passkey prompt. The product's name,
    /// identical in every environment, so it is a code default and not a shipped JSON line.</summary>
    public string ServerName { get; set; } = "ExpertToJob";

    /// <summary>Allowed origins for ceremonies (scheme + host + port), e.g. "http://localhost:5173".</summary>
    public string[] Origins { get; set; } = [];

    /// <summary>How long a registration/authentication challenge stays valid.</summary>
    public int ChallengeTimeoutSeconds { get; set; } = 300;
}
