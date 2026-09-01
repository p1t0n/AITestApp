using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>One row of an Expert's lawful-basis history, as it is read back.</summary>
public sealed record ProcessingRecordDto(
    Guid Id,
    Guid ExpertId,
    int Sequence,
    ProcessingOrigin Origin,
    LawfulBasis Basis,
    string? NoticeVersion,
    string Reason,
    DateTimeOffset RecordedAt);

/// <summary>
/// Reads and appends an Expert's lawful-basis history (P1T-183). There is no update and no delete on
/// this interface, and that absence is the design: a basis is superseded by a new row, never
/// rewritten (EDPB GL 05/2020 §123). The database enforces the same thing with a trigger, so a
/// caller reaching past this service does not get a second opinion.
///
/// <para>Ownership-scoped like every other roster service (P1T-182): an Expert reaches the history of
/// their own row and nothing else, and a row they do not own is 404 rather than 403. That matters
/// more here than elsewhere — a processing record names why we hold a specific person.</para>
/// </summary>
public interface IProcessingRecordService
{
    /// <summary>
    /// Appends a record. Used when the relationship genuinely changes — an approved claim moves a
    /// row from legitimate interest to pre-contractual necessity, a revocation moves it back
    /// (P1T-184). Both are new facts, not corrections of the old one.
    /// </summary>
    Task<ProcessingRecordDto> AppendAsync(
        Guid expertId, ProcessingOrigin origin, string? noticeVersion, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Appends the acknowledgment of a new notice version, keeping the basis exactly where it is.
    /// A person reading an updated notice is not a change of lawful basis, and pretending it were
    /// would put a row onto a ground nothing happened to justify.
    /// </summary>
    Task<ProcessingRecordDto> AcknowledgeNoticeAsync(
        Guid expertId, string noticeVersion, CancellationToken ct = default);

    /// <summary>The record in force: the highest sequence for this row.</summary>
    Task<ProcessingRecordDto> CurrentAsync(Guid expertId, CancellationToken ct = default);

    /// <summary>The whole history, oldest first. What the Art. 15 access view reads.</summary>
    Task<IReadOnlyList<ProcessingRecordDto>> HistoryAsync(Guid expertId, CancellationToken ct = default);
}

public class ProcessingRecordService(
    IAppDbContext db, IOwnershipScopeProvider scope, TimeProvider clock) : IProcessingRecordService
{
    public async Task<ProcessingRecordDto> AppendAsync(
        Guid expertId, ProcessingOrigin origin, string? noticeVersion, string reason,
        CancellationToken ct = default)
    {
        // Reachability first, before any validation: a caller who cannot see the row must not learn
        // from the shape of the error that it is there.
        await EnsureReachableAsync(expertId, ct);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A processing record states why it was written; the data subject reads it.", nameof(reason));
        }

        if (noticeVersion is not null && !TransparencyNotice.IsPublished(noticeVersion))
        {
            throw new ArgumentException(
                $"'{noticeVersion}' is not a published transparency-notice version. Recording an " +
                "unrecoverable version would make the acknowledgment unprovable.", nameof(noticeVersion));
        }

        return await AppendUncheckedAsync(expertId, origin, noticeVersion, reason, ct);
    }

    public async Task<ProcessingRecordDto> AcknowledgeNoticeAsync(
        Guid expertId, string noticeVersion, CancellationToken ct = default)
    {
        var current = await CurrentAsync(expertId, ct);

        if (!TransparencyNotice.IsPublished(noticeVersion))
        {
            throw new ArgumentException(
                $"'{noticeVersion}' is not a published transparency-notice version.", nameof(noticeVersion));
        }

        return await AppendUncheckedAsync(
            expertId,
            // The basis stays where it is. Only the acknowledged version moves.
            current.Origin,
            noticeVersion,
            $"Transparency notice {noticeVersion} acknowledged.",
            ct);
    }

    public async Task<ProcessingRecordDto> CurrentAsync(Guid expertId, CancellationToken ct = default)
    {
        await EnsureReachableAsync(expertId, ct);

        return await db.ProcessingRecords
                   .AsNoTracking()
                   .Where(r => r.ExpertId == expertId)
                   .OrderByDescending(r => r.Sequence)
                   .Select(r => new ProcessingRecordDto(
                       r.Id, r.ExpertId, r.Sequence, r.Origin, r.Basis,
                       r.NoticeVersion, r.Reason, r.RecordedAt))
                   .FirstOrDefaultAsync(ct)
               // Reachable but with no basis on file is a compliance defect, not an ordinary empty
               // result — every creation path writes the first record in the same transaction.
               ?? throw new NotFoundException(nameof(ProcessingRecord), expertId);
    }

    public async Task<IReadOnlyList<ProcessingRecordDto>> HistoryAsync(
        Guid expertId, CancellationToken ct = default)
    {
        await EnsureReachableAsync(expertId, ct);

        return await db.ProcessingRecords
            .AsNoTracking()
            .Where(r => r.ExpertId == expertId)
            .OrderBy(r => r.Sequence)
            .Select(r => new ProcessingRecordDto(
                r.Id, r.ExpertId, r.Sequence, r.Origin, r.Basis,
                r.NoticeVersion, r.Reason, r.RecordedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The append itself, past the ownership check the caller has already made. Split out so
    /// <see cref="AcknowledgeNoticeAsync"/> does not resolve the same row twice.
    /// </summary>
    private async Task<ProcessingRecordDto> AppendUncheckedAsync(
        Guid expertId, ProcessingOrigin origin, string? noticeVersion, string reason, CancellationToken ct)
    {
        var last = await db.ProcessingRecords
            .Where(r => r.ExpertId == expertId)
            .MaxAsync(r => (int?)r.Sequence, ct) ?? 0;

        var record = ProcessingRecord.For(
            expertId, last + 1, origin, noticeVersion, reason.Trim(), clock.GetUtcNow());

        db.ProcessingRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return Project(record);
    }

    /// <summary>
    /// Resolves the row through the caller's ownership scope. Throws <see cref="NotFoundException"/>
    /// for both "no such row" and "not yours" — the caller cannot tell them apart, which is what
    /// stops this surface confirming that some stranger is on the bench.
    /// </summary>
    private async Task EnsureReachableAsync(Guid expertId, CancellationToken ct)
    {
        var (unrestricted, owned) = await scope.CurrentAsync(ct);
        var exists = await db.Experts
            .AsNoTracking()
            .AnyAsync(e => e.Id == expertId && (unrestricted || e.Id == owned), ct);

        if (!exists)
        {
            throw new NotFoundException(nameof(Expert), expertId);
        }
    }

    private static ProcessingRecordDto Project(ProcessingRecord r) => new(
        r.Id, r.ExpertId, r.Sequence, r.Origin, r.Basis, r.NoticeVersion, r.Reason, r.RecordedAt);
}
