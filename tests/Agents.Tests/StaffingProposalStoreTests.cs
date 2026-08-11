using CvManager.Agents.Staffing;
using CvManager.Domain.Entities;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CvManager.Agents.Tests;

/// <summary>
/// Unit tests for the proposal ledger (P1T-100): creation snapshots the report deterministically,
/// listing serves the approval inbox, and a decision is a one-shot human act.
/// </summary>
public class StaffingProposalStoreTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"proposals-{Guid.NewGuid()}")
            .Options);

    private static StaffingProposalStore Store(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<StaffingProposalStore>.Instance);

    private static StaffingReport Report(bool degraded = false) => new(
        ["kafka", "kubernetes"],
        [
            new StaffingCandidate(
                Ada, "Ada Lovelace", "Platform Lead",
                new StaffingShortlistDetail(0.91, new(2, 3), []),
                new StaffingMatchDetail(StaffingMatchStatus.Completed, 78, "Strong", "answer", null),
                "Best coverage."),
            new StaffingCandidate(
                Grace, "Grace Hopper", "Engineer",
                new StaffingShortlistDetail(0.85, new(1, 3), []),
                new StaffingMatchDetail(StaffingMatchStatus.Failed, null, null, null, "model error"),
                "Solid depth."),
        ],
        new StaffingRecommendation(Ada, "Ada is the strongest fit."),
        degraded,
        degraded ? ["match failed"] : []);

    [Fact]
    public async Task CreateAsync_persists_a_pending_proposal_snapshotting_the_report()
    {
        await using var db = NewDb();
        var requester = Guid.NewGuid();

        var id = await Store(db).CreateAsync(requester, "Platform engineer.", Report(degraded: true));

        id.Should().NotBeNull();
        var proposal = await db.StaffingProposals.Include(p => p.Candidates).SingleAsync();
        proposal.Id.Should().Be(id!.Value);
        proposal.Status.Should().Be(StaffingProposalStatus.Pending);
        proposal.RequestedByUserId.Should().Be(requester);
        proposal.JobDescription.Should().Be("Platform engineer.");
        proposal.RecommendedEmployeeId.Should().Be(Ada);
        proposal.ReportDegraded.Should().BeTrue();
        proposal.Candidates.Should().HaveCount(2);
        var first = proposal.Candidates.Single(c => c.Rank == 1);
        first.EmployeeId.Should().Be(Ada);
        first.MatchScore.Should().Be(78);
        first.MatchBand.Should().Be("Strong");
        first.Rationale.Should().Be("Best coverage.");
        proposal.Candidates.Single(c => c.Rank == 2).MatchScore.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_swallows_persistence_failures_and_returns_null()
    {
        var db = NewDb();
        await db.DisposeAsync(); // a dead context makes SaveChanges throw

        var id = await Store(db).CreateAsync(null, "JD", Report());

        id.Should().BeNull();
    }

    [Fact]
    public async Task DecideAsync_approves_a_pending_proposal_exactly_once()
    {
        await using var db = NewDb();
        var store = Store(db);
        var id = (await store.CreateAsync(null, "JD", Report()))!.Value;
        var approver = Guid.NewGuid();

        var (result, proposal) = await store.DecideAsync(id, approver, approve: true, note: "  go ahead  ");

        result.Should().Be(ProposalDecisionResult.Decided);
        proposal!.Status.Should().Be(StaffingProposalStatus.Approved);
        proposal.DecidedByUserId.Should().Be(approver);
        proposal.DecidedAt.Should().NotBeNull();
        proposal.DecisionNote.Should().Be("go ahead");

        var (second, unchanged) = await store.DecideAsync(id, Guid.NewGuid(), approve: false, note: null);
        second.Should().Be(ProposalDecisionResult.AlreadyDecided);
        unchanged!.Status.Should().Be(StaffingProposalStatus.Approved, "the first decision stands");
    }

    [Fact]
    public async Task DecideAsync_rejects_and_reports_missing_proposals()
    {
        await using var db = NewDb();
        var store = Store(db);
        var id = (await store.CreateAsync(null, "JD", Report()))!.Value;

        var (missing, _) = await store.DecideAsync(Guid.NewGuid(), Guid.NewGuid(), true, null);
        missing.Should().Be(ProposalDecisionResult.NotFound);

        var (result, proposal) = await store.DecideAsync(id, Guid.NewGuid(), approve: false, note: null);
        result.Should().Be(ProposalDecisionResult.Decided);
        proposal!.Status.Should().Be(StaffingProposalStatus.Rejected);
        proposal.DecisionNote.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_filters_by_status_newest_first_with_ranked_candidates()
    {
        await using var db = NewDb();
        var store = Store(db);
        var first = (await store.CreateAsync(null, "JD one", Report()))!.Value;
        var second = (await store.CreateAsync(null, "JD two", Report()))!.Value;
        await store.DecideAsync(first, Guid.NewGuid(), approve: true, note: null);

        var pending = await store.ListAsync(StaffingProposalStatus.Pending);
        pending.Should().ContainSingle().Which.Id.Should().Be(second);
        pending[0].Candidates.Select(c => c.Rank).Should().BeInAscendingOrder();

        var all = await store.ListAsync();
        all.Should().HaveCount(2);

        var approved = await store.ListAsync("Approved"); // case-insensitive
        approved.Should().ContainSingle().Which.Id.Should().Be(first);
    }
}
