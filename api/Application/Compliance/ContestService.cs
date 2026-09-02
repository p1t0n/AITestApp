using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>One contested score, as the Service Manager's queue shows it.</summary>
/// <param name="ScoringCandidateId">The scan row the decision lives on.</param>
/// <param name="View">What the person said about it — read this before the score.</param>
public sealed record ContestQueueItemDto(
    Guid ScoringCandidateId,
    Guid ExpertId,
    string ExpertName,
    string JobDescription,
    int? Score,
    string? Band,
    string? Rationale,
    string? View,
    DateTimeOffset ContestedAt);

/// <summary>What a human concluded, and what they said back.</summary>
public sealed record ContestReviewDto(
    Guid ScoringCandidateId,
    string Outcome,
    string? Response,
    DateTimeOffset ReviewedAt,
    Guid? ReviewedByUserId);

/// <summary>
/// The Art. 22(3) safeguards (P1T-189). We concede that the roster scan makes automated decisions
/// and rely on Art. 22(2)(a) — contract necessity — which turns three safeguards from good practice
/// into obligations: <b>human intervention, the right to express a view, and the right to
/// contest</b>. All three are this service.
///
/// <para>Deliberately <b>not</b> an appeals workflow and <b>not</b> an SLA. What is owed is that a
/// person can ask for a human to look, say why, and have the outcome recorded — nothing here has
/// states, deadlines or escalation, because none of that is what the Article asks for and all of it
/// would be machinery nobody maintains.</para>
///
/// <para>The necessity argument this all rests on is written down in
/// <c>manuals/art22-safeguards.md</c>, not only in the ticket that decided it.</para>
/// </summary>
public interface IContestService
{
    /// <summary>
    /// Asks for a human to look at one score, with the person's own view of it. Their own row only:
    /// somebody else's score is a 404, exactly as everywhere else (P1T-182).
    /// </summary>
    Task<ContestQueueItemDto> ContestAsync(
        Guid scoringCandidateId, string? view, CancellationToken ct = default);

    /// <summary>Everything waiting for a human, oldest first.</summary>
    Task<IReadOnlyList<ContestQueueItemDto>> OpenAsync(CancellationToken ct = default);

    /// <summary>Records that a human looked and what they concluded.</summary>
    Task<ContestReviewDto> ReviewAsync(
        Guid scoringCandidateId, string outcome, string? response, Guid reviewedByUserId,
        CancellationToken ct = default);
}

public class ContestService(
    IAppDbContext db, IOwnershipScopeProvider scope, TimeProvider clock) : IContestService
{
    public async Task<ContestQueueItemDto> ContestAsync(
        Guid scoringCandidateId, string? view, CancellationToken ct = default)
    {
        // Ownership first: a scan row is a decision about one person, and which person is asking is
        // the whole of whether they may see it.
        var (unrestricted, owned) = await scope.CurrentAsync(ct);
        var candidate = await db.ScoringJobCandidates
            .FirstOrDefaultAsync(
                c => c.Id == scoringCandidateId && (unrestricted || c.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(ScoringJobCandidate), scoringCandidateId);

        if (candidate.ContestedAt is null)
        {
            candidate.ContestedAt = clock.GetUtcNow();
        }

        // A second contest replaces the view rather than queueing another item: the person is
        // saying more about the same score, not asking twice.
        if (!string.IsNullOrWhiteSpace(view))
        {
            candidate.ContestNote = view.Trim();
        }

        // Asking again after a review reopens it — a human looked, the person still disagrees, and
        // refusing to hear that would make the safeguard a one-shot form.
        candidate.ContestReviewedAt = null;
        candidate.ContestReviewedByUserId = null;
        candidate.ContestOutcome = null;
        candidate.ContestResponse = null;

        await db.SaveChangesAsync(ct);
        return await ReadBackAsync(scoringCandidateId, ct);
    }

    public async Task<IReadOnlyList<ContestQueueItemDto>> OpenAsync(CancellationToken ct = default)
    {
        return await db.ScoringJobCandidates
            .AsNoTracking()
            .Where(c => c.ContestedAt != null && c.ContestReviewedAt == null)
            .OrderBy(c => c.ContestedAt)
            .Join(db.ScoringJobs, c => c.JobId, j => j.Id, (c, j) => new ContestQueueItemDto(
                c.Id, c.ExpertId, c.Name, j.JobDescription,
                c.Score, c.Band, c.Rationale, c.ContestNote, c.ContestedAt!.Value))
            .ToListAsync(ct);
    }

    public async Task<ContestReviewDto> ReviewAsync(
        Guid scoringCandidateId, string outcome, string? response, Guid reviewedByUserId,
        CancellationToken ct = default)
    {
        if (outcome is not (ContestOutcome.Upheld or ContestOutcome.Overturned))
        {
            throw new ConflictException(
                $"'{outcome}' is not an outcome. A review either lets the score stand or does not.");
        }

        var candidate = await db.ScoringJobCandidates
            .FirstOrDefaultAsync(c => c.Id == scoringCandidateId, ct)
            ?? throw new NotFoundException(nameof(ScoringJobCandidate), scoringCandidateId);

        if (candidate.ContestedAt is null)
        {
            throw new ConflictException("Nobody has contested this score, so there is nothing to review.");
        }

        candidate.ContestReviewedAt = clock.GetUtcNow();
        candidate.ContestReviewedByUserId = reviewedByUserId;
        candidate.ContestOutcome = outcome;
        candidate.ContestResponse = string.IsNullOrWhiteSpace(response) ? null : response.Trim();

        await db.SaveChangesAsync(ct);

        return new ContestReviewDto(
            candidate.Id, outcome, candidate.ContestResponse,
            candidate.ContestReviewedAt!.Value, reviewedByUserId);
    }

    private async Task<ContestQueueItemDto> ReadBackAsync(Guid id, CancellationToken ct) =>
        await db.ScoringJobCandidates
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Join(db.ScoringJobs, c => c.JobId, j => j.Id, (c, j) => new ContestQueueItemDto(
                c.Id, c.ExpertId, c.Name, j.JobDescription,
                c.Score, c.Band, c.Rationale, c.ContestNote, c.ContestedAt!.Value))
            .FirstAsync(ct);
}
