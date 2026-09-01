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
}

/// <summary>
/// Symmetric (HS256) session-token settings. The signing key MUST match in the Web and Agents
/// configuration — Web issues tokens, both services validate them. Keep the production key in a
/// secret store, not source control.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>HS256 signing key. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "experttojob";
    public string Audience { get; set; } = "experttojob-app";
    public int AccessTokenMinutes { get; set; } = 60;
}

/// <summary>WebAuthn relying-party settings for fido2-net-lib.</summary>
public sealed class PasskeyOptions
{
    /// <summary>Relying-party id — the registrable domain (no scheme/port), e.g. "localhost".</summary>
    public string ServerDomain { get; set; } = "localhost";

    /// <summary>Human-readable relying-party name shown in the passkey prompt.</summary>
    public string ServerName { get; set; } = "ExpertToJob";

    /// <summary>Allowed origins for ceremonies (scheme + host + port), e.g. "http://localhost:5173".</summary>
    public string[] Origins { get; set; } = [];

    /// <summary>How long a registration/authentication challenge stays valid.</summary>
    public int ChallengeTimeoutSeconds { get; set; } = 300;
}
