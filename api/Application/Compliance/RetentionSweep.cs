using ExpertToJob.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>What one pass did.</summary>
/// <param name="Examined">Records whose period was evaluated.</param>
/// <param name="Expired">Records deleted because their period had run out.</param>
public sealed record RetentionSweepResult(int Examined, int Expired);

/// <summary>
/// One pass of the retention sweep (P1T-188): find the records whose period has run out and erase
/// them <b>through the erasure path</b> rather than deleting them here.
///
/// <para>That is the whole design of this class. Retention is a trigger, not a second mechanism —
/// the scrub of the decision ledger, the typed package rewrite and the store declaration all live
/// in one place, and a parallel implementation would drift from it. It is only a question of
/// when.</para>
/// </summary>
public interface IRetentionSweep
{
    Task<RetentionSweepResult> RunOnceAsync(CancellationToken ct = default);
}

public class RetentionSweep(
    IAppDbContext db, IRetentionErasure erasure, TimeProvider clock) : IRetentionSweep
{
    public async Task<RetentionSweepResult> RunOnceAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        // Collection date is the first ProcessingRecord — Art. 5(1)(e)'s reference point, and for an
        // unclaimed record the only date in the system at all. Read alongside the row so the policy
        // can be a pure function of facts rather than of queries.
        var candidates = await db.Experts
            .AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Email,
                IsClaimed = e.OwnerUserId != null,
                e.LastActivityAt,
                CollectedAt = e.ProcessingRecords
                    .OrderBy(r => r.Sequence)
                    .Select(r => (DateTimeOffset?)r.RecordedAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var expired = 0;
        foreach (var candidate in candidates)
        {
            // No basis on file is a compliance defect, not an expiry: something is wrong with that
            // row and deleting it would destroy the evidence of what. The structural checks in
            // ProcessingRecordTests are what catch it.
            if (candidate.CollectedAt is not { } collectedAt)
            {
                continue;
            }

            var verdict = RetentionPolicy.For(
                candidate.Email, candidate.IsClaimed, collectedAt, candidate.LastActivityAt);

            if (!verdict.IsExpiredAt(now))
            {
                continue;
            }

            await erasure.EraseExpiredAsync(candidate.Id, ct);
            expired++;
        }

        return new RetentionSweepResult(candidates.Count, expired);
    }
}
