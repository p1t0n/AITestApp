using System.Security.Claims;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Auth;

/// <summary>
/// The claim names carried by the shared session JWT. Both hosts mint against these (Web) and read
/// them (Web and Agents), so the strings live once, in the layer both hosts reference — the JWT
/// *validation* parameters are duplicated per host by design, but the vocabulary is not.
/// </summary>
public static class SessionClaims
{
    /// <summary>Subject: the user id. The token's identity.</summary>
    public const string Subject = "sub";

    /// <summary>The account's <see cref="UserRole"/>, by name.</summary>
    public const string Role = "role";

    /// <summary>The account's session generation — see <c>User.TokenVersion</c>.</summary>
    public const string TokenVersion = "tv";
}

/// <summary>
/// Authorization policy names. A policy, not a bare role string, so an endpoint's declaration reads
/// as an audience ("this is staff") and the requirements behind it can grow.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Staff. The default and the fallback: an endpoint that says nothing is staff-only.</summary>
    public const string ServiceManager = nameof(UserRole.ServiceManager);

    /// <summary>The person the CV is about. Opt-in, always explicit on the endpoint.</summary>
    public const string Expert = nameof(UserRole.Expert);
}

/// <summary>
/// The revocation half of session validation: a token is only current while the account it names
/// still exists, is still active, and still carries the token version the token was minted with.
///
/// <para>Lifetime alone cannot express revocation — a deleted person's session would survive up to
/// <c>AccessTokenMinutes</c> — so this runs per request in both hosts, from their own JWT bearer
/// event. Shared here because "what makes a session current" must not drift between the two.</para>
/// </summary>
public static class SessionRevocation
{
    /// <summary>
    /// Checks the principal against the account it names. Returns <c>null</c> when the session is
    /// current, otherwise the reason it is not — the caller turns that into a 401.
    /// </summary>
    public static async Task<string?> CheckAsync(
        IAppDbContext db, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var userId = UserId(principal);
        if (userId is null)
        {
            return "The session token carries no usable subject.";
        }

        var minted = TokenVersionOf(principal);
        if (minted is null)
        {
            // A token minted before the split, or one hand-rolled without the claim. Either way it
            // cannot be checked for revocation, so it is not accepted.
            return $"The session token carries no '{SessionClaims.TokenVersion}' claim.";
        }

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TokenVersion, u.Status })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return "The account this session names no longer exists.";
        }

        if (account.Status != UserStatus.Active)
        {
            return "The account this session names is not active.";
        }

        return account.TokenVersion == minted.Value
            ? null
            : "The session was revoked (token version superseded).";
    }

    /// <summary>The user id the token names, or null when it carries none that parses.</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(SessionClaims.Subject)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>The token version the token was minted with, or null when it carries none.</summary>
    public static int? TokenVersionOf(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirst(SessionClaims.TokenVersion)?.Value, out var v) ? v : null;
}
