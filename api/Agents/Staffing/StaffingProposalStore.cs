using CvManager.Application.Abstractions;
using CvManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvManager.Agents.Staffing;

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
        CvManager.Agents.Handoff.HandoffPackage package,
        CancellationToken ct = default)
    {
        try
        {
            var proposal = new StaffingProposal
            {
                Id = Guid.NewGuid(),
                RequestedByUserId = requestedBy,
                JobDescription = jobDescription,
                RecommendedEmployeeId = report.Recommendation?.EmployeeId,
                ReportDegraded = report.Degraded,
                Status = StaffingProposalStatus.Pending,
                CreatedAt = clock.GetUtcNow(),
                PackageJson = StaffingHandoffDocument.From(package, report).Serialize(),
                Candidates = report.Candidates.Select((c, i) => new StaffingProposalCandidate
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = c.EmployeeId,
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
