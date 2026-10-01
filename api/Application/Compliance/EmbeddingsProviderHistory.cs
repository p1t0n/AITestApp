using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>
/// The deployment's embeddings provider history: read by the access view, written by the host that
/// actually runs the embedder (EXP-66, <c>manuals/adr-embeddings-provider-seam.md</c> §2
/// decision 17).
/// </summary>
public sealed class EmbeddingsProviderHistory(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Every period this deployment has had, oldest first.</summary>
    public async Task<IReadOnlyList<EmbeddingsProviderPeriod>> PeriodsAsync(
        CancellationToken ct = default)
    {
        // Loaded whole and ordered in memory. The table has one row per provider switch this
        // deployment has ever made — single digits, forever — and "since the beginning" is a null
        // start date, which SQL would sort last under an ordinary ASC rather than first.
        var periods = await db.EmbeddingsProviderPeriods.AsNoTracking().ToListAsync(ct);

        return [.. periods.OrderBy(p => p.StartedAt ?? DateTimeOffset.MinValue)];
    }

    /// <summary>
    /// Records that <paramref name="provider"/> is the provider running now. Closes the open period
    /// and opens a new one when that is a change, and writes nothing at all when it is not — which
    /// is every ordinary restart.
    /// </summary>
    public async Task RecordActiveProviderAsync(string provider, CancellationToken ct = default)
    {
        var periods = await db.EmbeddingsProviderPeriods.ToListAsync(ct);
        var current = Latest(periods);

        if (string.Equals(current?.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (current is not null)
        {
            current.EndedAt = now;
        }

        db.EmbeddingsProviderPeriods.Add(new EmbeddingsProviderPeriod
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            // Null only ever means "since the beginning", which is the migration's seed and never
            // something a running host may claim: it knows exactly when it started.
            StartedAt = now,
            EndedAt = null,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The period in force: the open one if there is one, otherwise the one that ended most
    /// recently. Written this way rather than "the last row" because the table is append-only but
    /// not insertion-ordered — nothing stops two rows sharing a timestamp, and a history read back
    /// in the wrong order would date somebody's disclosure wrongly.
    /// </summary>
    private static EmbeddingsProviderPeriod? Latest(IReadOnlyList<EmbeddingsProviderPeriod> periods) =>
        periods.FirstOrDefault(p => p.EndedAt is null)
        ?? periods.OrderByDescending(p => p.EndedAt).FirstOrDefault();
}
