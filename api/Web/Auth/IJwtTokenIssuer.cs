using CvManager.Domain.Entities;

namespace CvManager.Web.Auth;

/// <summary>Issues session JWTs after a successful passkey ceremony.</summary>
public interface IJwtTokenIssuer
{
    /// <summary>Mints a signed session token for the user. Returns the token and its expiry.</summary>
    (string Token, DateTimeOffset ExpiresAt) Issue(User user);
}
