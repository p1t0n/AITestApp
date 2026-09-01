using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Resolves the caller's ownership scope from their session (P1T-182). A Service Manager reaches
/// the whole roster; an Expert reaches the one row their account owns, and nothing if no row does.
///
/// <para>Scoped and memoized: every service touched by one request asks, and the answer cannot
/// change mid-request — the role is a token claim and the owned row is one lookup.</para>
///
/// <para>No principal at all — a background call, or a path that somehow skipped authentication —
/// resolves to <see cref="OwnershipScope.OwnedBy"/> nothing rather than to unrestricted. If this
/// ever runs where it was not expected, it must fail closed.</para>
/// </summary>
public sealed class HttpOwnershipScopeProvider(
    IHttpContextAccessor accessor, IAppDbContext db) : IOwnershipScopeProvider
{
    private OwnershipScope? _resolved;

    public async ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return _resolved = OwnershipScope.OwnedBy(null);
        }

        if (user.IsInRole(AuthPolicies.ServiceManager))
        {
            return _resolved = OwnershipScope.Unrestricted;
        }

        var userId = SessionRevocation.UserId(user);
        if (userId is null)
        {
            return _resolved = OwnershipScope.OwnedBy(null);
        }

        // At most one row can name this account — the unique partial index on OwnerUserId says so —
        // so the first match is the only one.
        var ownedExpertId = await db.Experts
            .AsNoTracking()
            .Where(e => e.OwnerUserId == userId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        return _resolved = OwnershipScope.OwnedBy(ownedExpertId);
    }
}
