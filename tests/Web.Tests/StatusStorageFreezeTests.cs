using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The persisted form of the status columns, frozen as literals (EXP-76).
///
/// <para>These rows outlive any refactor of the types that write them: a scan candidate stores
/// <c>'pending'</c>, a reviewed contest stores <c>'upheld'</c>, a decided proposal stores
/// <c>'approved'</c>, and there is no migration in the world that goes back and rewrites them. So
/// the freeze is a round trip through the real schema: raw SQL puts the exact string in the column,
/// EF reads the row, EF writes it back, and raw SQL reads the column again. Whatever CLR type sits
/// in between — a <c>string</c>, a closed hierarchy, an enum — has to hand back the same bytes.</para>
///
/// <para>Deliberately written without naming a single status constant. A test that spells the value
/// as <c>ScoringCandidateStatus.Scored</c> mirrors the code it is meant to pin and would follow it
/// anywhere; these literals cannot.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class StatusStorageFreezeTests(WebApiFactory factory)
{
    public static TheoryData<string> ScanCandidateStatuses() => new() { "pending", "scored", "failed" };

    public static TheoryData<string> ContestOutcomes() => new() { "upheld", "overturned" };

    public static TheoryData<string> ProposalStatuses() => new() { "pending", "approved", "rejected" };

    [Theory]
    [MemberData(nameof(ScanCandidateStatuses))]
    public async Task A_scan_candidate_status_round_trips_through_EF_as_the_same_stored_string(string stored)
    {
        var candidateId = await GivenAScanCandidateAsync();
        await SetColumnAsync("ScoringJobCandidates", "Status", candidateId, stored);

        await TouchScanCandidateAsync(candidateId);

        (await ReadColumnAsync("ScoringJobCandidates", "Status", candidateId)).Should().Be(stored);
    }

    [Theory]
    [MemberData(nameof(ContestOutcomes))]
    public async Task A_contest_outcome_round_trips_through_EF_as_the_same_stored_string(string stored)
    {
        var candidateId = await GivenAScanCandidateAsync();
        await SetColumnAsync("ScoringJobCandidates", "ContestOutcome", candidateId, stored);

        await TouchScanCandidateAsync(candidateId);

        (await ReadColumnAsync("ScoringJobCandidates", "ContestOutcome", candidateId)).Should().Be(stored);
    }

    [Theory]
    [MemberData(nameof(ProposalStatuses))]
    public async Task A_proposal_status_round_trips_through_EF_as_the_same_stored_string(string stored)
    {
        var proposalId = await GivenAProposalAsync();
        await SetColumnAsync("StaffingProposals", "Status", proposalId, stored);

        await TouchProposalAsync(proposalId);

        (await ReadColumnAsync("StaffingProposals", "Status", proposalId)).Should().Be(stored);
    }

    /// <summary>
    /// The other half of the round trip: the value EF writes for a row it created itself, read
    /// straight out of the column. A converter that only read correctly would pass everything
    /// above and still corrupt every new row.
    /// </summary>
    [Fact]
    public async Task A_freshly_seeded_scan_candidate_is_stored_as_pending()
    {
        var candidateId = await GivenAScanCandidateAsync();

        (await ReadColumnAsync("ScoringJobCandidates", "Status", candidateId)).Should().Be("pending");
    }

    /// <inheritdoc cref="A_freshly_seeded_scan_candidate_is_stored_as_pending"/>
    [Fact]
    public async Task A_freshly_seeded_proposal_is_stored_as_pending()
    {
        var proposalId = await GivenAProposalAsync();

        (await ReadColumnAsync("StaffingProposals", "Status", proposalId)).Should().Be("pending");
    }

    // ---- the world --------------------------------------------------------------------------

    /// <summary>A scan candidate on a real job, hung off a real Expert (the row cascades with one).
    /// Status is left at whatever the entity defaults to — the point of the test above.</summary>
    private async Task<Guid> GivenAScanCandidateAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expertId = await db.Experts.AsNoTracking().Select(e => e.Id).FirstAsync();
        var candidateId = Guid.NewGuid();
        var job = new ScoringJob
        {
            Id = Guid.NewGuid(),
            JobDescription = "Status freeze",
            ChunkSize = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        job.Candidates.Add(new ScoringJobCandidate
        {
            Id = candidateId,
            ExpertId = expertId,
            Name = "Status freeze",
            Title = "Engineer",
            Digest = "A digest.",
        });
        db.ScoringJobs.Add(job);
        await db.SaveChangesAsync();
        return candidateId;
    }

    private async Task<Guid> GivenAProposalAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var proposalId = Guid.NewGuid();
        db.StaffingProposals.Add(new StaffingProposal
        {
            Id = proposalId,
            JobDescription = "Status freeze",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return proposalId;
    }

    /// <summary>Loads the row through EF and saves it again, so the status makes the full
    /// read-materialise-write trip rather than only being read.</summary>
    private async Task TouchScanCandidateAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ScoringJobCandidates.SingleAsync(c => c.Id == id);
        row.Rationale = $"Touched at {DateTimeOffset.UtcNow:O}.";
        await db.SaveChangesAsync();
    }

    /// <inheritdoc cref="TouchScanCandidateAsync"/>
    private async Task TouchProposalAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.StaffingProposals.SingleAsync(p => p.Id == id);
        row.DecisionNote = $"Touched at {DateTimeOffset.UtcNow:O}.";
        await db.SaveChangesAsync();
    }

    private async Task SetColumnAsync(string table, string column, Guid id, string value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Built into a local on purpose: EF1002 fires on an interpolated literal passed straight
        // into ExecuteSqlRaw, and the table/column names here are this file's own constants while
        // the only caller-supplied value travels as the {0} parameter.
        var sql = $$"""UPDATE "{{table}}" SET "{{column}}" = {0} WHERE "Id" = {1}""";
        await db.Database.ExecuteSqlRawAsync(sql, value, id);
    }

    private async Task<string?> ReadColumnAsync(string table, string column, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sql = $$"""SELECT "{{column}}" AS "Value" FROM "{{table}}" WHERE "Id" = {0}""";
        var values = await db.Database.SqlQueryRaw<string?>(sql, id).ToListAsync();
        return values.Single();
    }
}
