using ExpertToJob.Application.Auth;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExpertToJob.Web.Compliance;

/// <summary>
/// Stamps <see cref="Expert.LastActivityAt"/> whenever an Expert writes something of their own
/// (P1T-188) — and, just as importantly, whenever anybody else writes it, does not.
///
/// <para><b>Why an interceptor rather than a line in each service.</b> The rule that carries this
/// slice is the negative one: a Service Manager's edit and an agent's scoring must <em>not</em>
/// move the clock, or a bench running weekly scans would keep everybody alive by looking at them.
/// A stamp written into eleven controllers is a rule the twelfth forgets; here there is one place,
/// it reads the ownership scope that already exists, and a new write path is covered the day it is
/// written. Ownership answers the question exactly: in this host a Service Manager resolves to
/// <c>Unrestricted</c> and an Expert to <c>OwnedBy(their row)</c>, and the MCP host — every
/// agent — is <c>Unrestricted</c> for every caller.</para>
///
/// <para>Reads never stamp: this only fires when something is actually being saved.</para>
/// </summary>
public sealed class ExpertActivityInterceptor(
    IServiceProvider services, TimeProvider clock) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await StampAsync(context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task StampAsync(DbContext context, CancellationToken ct)
    {
        var entries = context.ChangeTracker.Entries().ToList();
        if (!entries.Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            return;
        }

        var scope = await services.GetRequiredService<IOwnershipScopeProvider>().CurrentAsync(ct);
        if (scope.IsUnrestricted || scope.ExpertId is not { } expertId)
        {
            // Staff, an agent, or somebody who owns no record. None of them is the person, so none
            // of them keeps the record alive.
            return;
        }

        // Their own row is often already tracked by the write in flight; loading it otherwise costs
        // one keyed lookup and joins the same transaction.
        var expert = await context.Set<Expert>().FindAsync([expertId], ct);
        if (expert is null || context.Entry(expert).State == EntityState.Deleted)
        {
            // Erasure is in progress. Stamping a row on its way out would be noise at best.
            return;
        }

        // Truncated to what Postgres stores, so the value read back is the value written — the same
        // trap the pause timestamp hit in P1T-185, and the retention boundary compares against this.
        var now = clock.GetUtcNow();
        expert.LastActivityAt = new DateTimeOffset(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond), now.Offset);
    }
}
