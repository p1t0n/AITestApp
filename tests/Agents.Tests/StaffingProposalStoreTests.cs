using CvManager.Agents.Agents;
using CvManager.Agents.Handoff;
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

    /// <summary>A representative accumulated package: provenance with a caps snapshot, one slice
    /// per stage, and a degradation entry only for degraded runs (mirroring the report's notes).</summary>
    private static HandoffPackage Package(bool degraded = false) => new(
        new Dictionary<string, string?> { ["jobDescription"] = "Platform engineer.", ["matchTop"] = "2" },
        new RunProvenance(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            [new CapWindowSnapshot("daily", 1_000, 50_000, DateTimeOffset.Parse("2026-08-17T00:00:00Z"))],
            DateTimeOffset.Parse("2026-08-16T12:00:00Z")),
        [
            new StageSlice(
                "shortlist", "agent-shortlist", ["mcp:read", "mcp:search"], "gemini-3.5-flash-lite",
                100, 20, DateTimeOffset.Parse("2026-08-16T12:00:01Z"), DateTimeOffset.Parse("2026-08-16T12:00:03Z"),
                StageSliceStatus.Completed),
            new StageSlice(
                "match", "agent-match", ["mcp:read"], null,
                degraded ? 0 : 200, degraded ? 0 : 50,
                DateTimeOffset.Parse("2026-08-16T12:00:03Z"), DateTimeOffset.Parse("2026-08-16T12:00:06Z"),
                degraded ? StageSliceStatus.Failed : StageSliceStatus.Completed,
                degraded ? "model error" : null, RetryCount: degraded ? 2 : 0),
        ],
        degraded ? [new DegradationEntry("match", "The match assessment for Grace Hopper", "model error")] : []);

    [Fact]
    public async Task CreateAsync_persists_a_pending_proposal_snapshotting_the_report()
    {
        await using var db = NewDb();
        var requester = Guid.NewGuid();

        var id = await Store(db).CreateAsync(requester, "Platform engineer.", Report(degraded: true), Package(degraded: true));

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

        var id = await Store(db).CreateAsync(null, "JD", Report(), Package());

        id.Should().BeNull();
    }

    [Fact]
    public async Task DecideAsync_approves_a_pending_proposal_exactly_once()
    {
        await using var db = NewDb();
        var store = Store(db);
        var id = (await store.CreateAsync(null, "JD", Report(), Package()))!.Value;
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
        var id = (await store.CreateAsync(null, "JD", Report(), Package()))!.Value;

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
        var first = (await store.CreateAsync(null, "JD one", Report(), Package()))!.Value;
        var second = (await store.CreateAsync(null, "JD two", Report(), Package()))!.Value;
        await store.DecideAsync(first, Guid.NewGuid(), approve: true, note: null);

        var pending = await store.ListAsync(StaffingProposalStatus.Pending);
        pending.Should().ContainSingle().Which.Id.Should().Be(second);
        pending[0].Candidates.Select(c => c.Rank).Should().BeInAscendingOrder();

        var all = await store.ListAsync();
        all.Should().HaveCount(2);

        var approved = await store.ListAsync("Approved"); // case-insensitive
        approved.Should().ContainSingle().Which.Id.Should().Be(first);
    }

    // ----- The persisted handoff document (P1T-133) -------------------------------------------

    [Fact]
    public async Task CreateAsync_persists_the_full_handoff_document_that_round_trips()
    {
        await using var db = NewDb();
        var report = Report();
        var package = Package();

        var id = await Store(db).CreateAsync(null, "Platform engineer.", report, package);

        var stored = (await db.StaffingProposals.SingleAsync()).PackageJson;
        var document = StaffingHandoffDocument.TryDeserialize(stored);
        document.Should().NotBeNull();
        // The report round-trips whole — same wire JSON in as out (plus its own proposal id,
        // stamped at creation so the drill-in matches the requester's SSE report), no truncation.
        System.Text.Json.JsonSerializer.Serialize(document!.Report, StaffingHandoffDocument.Json)
            .Should().Be(System.Text.Json.JsonSerializer.Serialize(
                report with { ProposalId = id }, StaffingHandoffDocument.Json));
        document.Inputs["jobDescription"].Should().Be("Platform engineer.");
        document.Provenance.CallerUserId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        document.Provenance.CapsSnapshotAtStart.Should().ContainSingle().Which.Window.Should().Be("daily");
        document.Slices.Should().HaveCount(2);
        document.Slices[0].Scopes.Should().Equal("mcp:read", "mcp:search");
        document.Degradations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_persisted_document_survives_a_restart()
    {
        var dbName = $"proposals-{Guid.NewGuid()}";
        AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options);

        Guid id;
        await using (var db = Db())
        {
            id = (await Store(db).CreateAsync(null, "JD", Report(), Package()))!.Value;
        }

        // A fresh context over the same store: nothing survives in memory but the row itself.
        await using (var db = Db())
        {
            var reloaded = await db.StaffingProposals.SingleAsync(p => p.Id == id);
            var document = StaffingHandoffDocument.TryDeserialize(reloaded.PackageJson);
            document.Should().NotBeNull();
            document!.Report.Candidates.Should().HaveCount(2);
            document.Slices.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task A_degraded_runs_document_marks_its_losses_explicitly()
    {
        await using var db = NewDb();

        await Store(db).CreateAsync(null, "JD", Report(degraded: true), Package(degraded: true));

        var document = StaffingHandoffDocument.TryDeserialize(
            (await db.StaffingProposals.SingleAsync()).PackageJson)!;
        document.Report.Degraded.Should().BeTrue();
        document.Report.Notes.Should().NotBeEmpty();
        document.Degradations.Should().NotBeEmpty(
            "degradation entries travel whenever the report carries notes");
        var failed = document.Slices.Should().ContainSingle(s => s.Status == StageSliceStatus.Failed).Subject;
        failed.RetryCount.Should().Be(2);
        failed.DegradeReason.Should().Be("model error");
    }

    [Fact]
    public void TryDeserialize_degrades_to_null_on_legacy_or_corrupt_columns()
    {
        StaffingHandoffDocument.TryDeserialize(null).Should().BeNull();
        StaffingHandoffDocument.TryDeserialize("").Should().BeNull();
        StaffingHandoffDocument.TryDeserialize("{not json").Should().BeNull();
    }

    /// <summary>Reflection gate: every public field of the wire <see cref="StaffingReport"/> must
    /// appear in the persisted document's report node — report growth can't silently outpace the
    /// package. The sample report populates every optional field so WhenWritingNull can't hide one.</summary>
    [Fact]
    public void The_persisted_report_carries_every_wire_report_field()
    {
        var fullReport = Report() with
        {
            ProposalId = Guid.NewGuid(),
            Extraction = new JdRequirements(
                [new JdRequirement("kafka", RequirementKind.Skill, RequirementPriority.MustHave, null, "kafka", false)],
                JdSeniority.Senior, null, []),
        };
        var document = StaffingHandoffDocument.From(Package(), fullReport);

        using var json = System.Text.Json.JsonDocument.Parse(document.Serialize());
        var reportKeys = json.RootElement.GetProperty("report").EnumerateObject()
            .Select(p => p.Name).ToHashSet();

        foreach (var property in typeof(StaffingReport).GetProperties())
        {
            var wireName = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            reportKeys.Should().Contain(wireName,
                $"the persisted package must carry StaffingReport.{property.Name}");
        }
    }
}
