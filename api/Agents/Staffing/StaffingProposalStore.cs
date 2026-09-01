using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Agents.Staffing;

/// <summary>What a decision attempt produced. Exactly one case applies.</summary>
public enum ProposalDecisionResult
{
    Decided,
    NotFound,
    AlreadyDecided,
}

/// <summary>
/// Persistence for staffing proposals (P1T-100). Creation is best-effort — a DB fault must not
/// sink a staffing run that already succeeded, so it returns null and the report ships without an
/// approval record (degrade, never fail). Decisions are the human write path and DO surface
/// errors: silently losing an approval would be worse than a 500.
/// </summary>
public sealed class StaffingProposalStore(
    IAppDbContext db,
    TimeProvider clock,
    ILogger<StaffingProposalStore> logger)
{
    /// <summary>Persists a run's report as a pending proposal, with the full handoff document
    /// (package + report, P1T-133) in the jsonb column. Returns the proposal id, or null when
    /// persistence failed (logged; the caller degrades the report instead of failing).</summary>
    public async Task<Guid?> CreateAsync(
        Guid? requestedBy,
        string jobDescription,
        StaffingReport report,
        ExpertToJob.Agents.Handoff.HandoffPackage package,
        CancellationToken ct = default)
    {
        try
        {
            var id = Guid.NewGuid();
            // The persisted report carries its own proposal id — the drill-in then serves exactly
            // what the requester's SSE report showed (which gains the same id after this call).
            var document = StaffingHandoffDocument.From(package, report with { ProposalId = id });
            var proposal = new StaffingProposal
            {
                Id = id,
                RequestedByUserId = requestedBy,
                JobDescription = jobDescription,
                RecommendedExpertId = report.Recommendation?.ExpertId,
                ReportDegraded = report.Degraded,
                Status = StaffingProposalStatus.Pending,
                CreatedAt = clock.GetUtcNow(),
                PackageJson = document.Serialize(),
                Candidates = report.Candidates.Select((c, i) => new StaffingProposalCandidate
                {
                    Id = Guid.NewGuid(),
                    ExpertId = c.ExpertId,
                    Name = c.Name,
                    Title = c.Title,
                    Rank = i + 1,
                    MatchScore = c.Match.Score,
                    MatchBand = c.Match.Band,
                    Rationale = c.Rationale,
                }).ToList(),
            };

            db.StaffingProposals.Add(proposal);
            await db.SaveChangesAsync(ct);
            return proposal.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist the staffing proposal; the report ships without one.");
            return null;
        }
    }

    /// <summary>One proposal with candidates in rank order, or null — the approver drill-in read
    /// (P1T-134). Read-only; the package column deserializes at the endpoint.</summary>
    public async Task<StaffingProposal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var proposal = await db.StaffingProposals.AsNoTracking()
            .Include(p => p.Candidates)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        proposal?.Candidates.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return proposal;
    }

    /// <summary>Proposals newest-first, optionally filtered to one status, candidates in rank order.</summary>
    public async Task<List<StaffingProposal>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        var query = db.StaffingProposals.AsNoTracking().Include(p => p.Candidates).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(p => p.Status == normalized);
        }

        var proposals = await query.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        foreach (var proposal in proposals)
        {
            proposal.Candidates.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        }

        return proposals;
    }

    /// <summary>
    /// Which of these people are no longer available — paused themselves, or gone from the bench
    /// (P1T-185). A pending proposal is <b>not</b> withdrawn when somebody pauses: hiding is not a
    /// retraction of a decision already put in front of a human, and a decision ledger keeps its
    /// rows. So the proposal stands and the candidate is badged, which is the honest middle: the
    /// approver can still read what was recommended and can see that acting on it may no longer be
    /// possible.
    /// </summary>
    public async Task<HashSet<Guid>> UnavailableAmongAsync(
        IEnumerable<Guid> expertIds, CancellationToken ct = default)
    {
        var ids = expertIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var available = await db.Experts
            .OnTheBench()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(ct);

        return ids.Except(available).ToHashSet();
    }

    /// <summary>Records a human decision. Only a pending proposal can be decided, and only once —
    /// a second decision reports <see cref="ProposalDecisionResult.AlreadyDecided"/> untouched.</summary>
    public async Task<(ProposalDecisionResult Result, StaffingProposal? Proposal)> DecideAsync(
        Guid id, Guid decidedBy, bool approve, string? note, CancellationToken ct = default)
    {
        var proposal = await db.StaffingProposals
            .Include(p => p.Candidates)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal is null)
        {
            return (ProposalDecisionResult.NotFound, null);
        }

        if (proposal.Status != StaffingProposalStatus.Pending)
        {
            return (ProposalDecisionResult.AlreadyDecided, proposal);
        }

        proposal.Status = approve ? StaffingProposalStatus.Approved : StaffingProposalStatus.Rejected;
        proposal.DecidedByUserId = decidedBy;
        proposal.DecidedAt = clock.GetUtcNow();
        proposal.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(ct);

        proposal.Candidates.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return (ProposalDecisionResult.Decided, proposal);
    }
}
