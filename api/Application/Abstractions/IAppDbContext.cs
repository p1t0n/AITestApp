using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// Persistence seam the Application layer depends on. Implemented by Infrastructure's
/// AppDbContext and substitutable (e.g. EF InMemory) in tests. Lets the future MCP server
/// reuse Application services without referencing Infrastructure directly.
/// </summary>
public interface IAppDbContext
{
    DbSet<Expert> Experts { get; }
    DbSet<SpokenLanguage> SpokenLanguages { get; }
    DbSet<AvailabilityEntry> AvailabilityEntries { get; }
    DbSet<Category> Categories { get; }
    DbSet<Skill> Skills { get; }
    DbSet<ExpertSkill> ExpertSkills { get; }
    DbSet<Qualification> Qualifications { get; }
    DbSet<Experience> Experiences { get; }
    DbSet<Achievement> Achievements { get; }
    DbSet<ExperienceSkill> ExperienceSkills { get; }
    DbSet<User> Users { get; }
    DbSet<PasskeyCredential> PasskeyCredentials { get; }
    DbSet<AgentUsage> AgentUsages { get; }
    DbSet<StaffingProposal> StaffingProposals { get; }
    DbSet<StaffingProposalCandidate> StaffingProposalCandidates { get; }
    DbSet<ScoringJob> ScoringJobs { get; }
    DbSet<ScoringJobCandidate> ScoringJobCandidates { get; }
    DbSet<ProcessingRecord> ProcessingRecords { get; }
    DbSet<PendingClaim> PendingClaims { get; }
    DbSet<ClaimCode> ClaimCodes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
