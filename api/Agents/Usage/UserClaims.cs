using System.Security.Claims;

namespace ExpertToJob.Agents.Usage;

public static class UserClaims
{
    /// <summary>The authenticated user's id from the session token's subject claim, or null.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
