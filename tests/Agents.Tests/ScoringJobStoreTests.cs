using CvManager.Agents.RosterScan;
using CvManager.Domain.Entities;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CvManager.Agents.Tests;

/// <summary>
/// ScoringJobStore semantics (P1T-122) over the in-memory provider: creation seeds pending rows,
/// state transitions are guarded (a terminal job can never resurrect), chunk results batch-write
/// durably, the resumable sweep finds due-paused + orphaned-running jobs, and progress counts
/// stay honest (failed counts as settled).
/// </summary>
public class ScoringJobStoreTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid Grace = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"scoring-jobs-{Guid.NewGuid()}")
            .Options);

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    private static ScoringJobStore Store(AppDbContext db) => new(db, Clock);

    private static Task<ScoringJob> CreateJobAsync(ScoringJobStore store) =>
        store.CreateAsync(
            requestedBy: null,
            jobDescription: "Senior engineer JD",
            extractionJson: """{"requirements":[]}""",
            filtersJson: null,
            chunkSize: 10,
            candidates:
            [
                new ScoringCandidateSeed(Ada, "Ada Lovelace", "Engineer"),
                new ScoringCandidateSeed(Grace, "Grace Hopper", "Admiral"),
            ]);

    [Fact]
    public async Task Create_seeds_a_queued_job_with_pending_candidates()
    {
        await using var db = NewDb();
        var job = await CreateJobAsync(Store(db));

        job.State.Should().Be(ScoringJobState.Queued);
        job.Candidates.Should().HaveCount(2).And.OnlyContain(c => c.Status == ScoringCandidateStatus.Pending);
        ScoringJobProgress.Of(job.Candidates).Should().Be(new ScoringJobProgress(0, 0, 2, 2));
    }

    [Fact]
    public async Task Transitions_follow_the_guard_map_and_terminal_states_never_resurrect()
    {
        await using var db = NewDb();
        var store = Store(db);
        var job = await CreateJobAsync(store);

        (await store.TryTransitionAsync(job.Id, ScoringJobState.Paused)).Should().BeFalse("queued cannot pause");
        (await store.TryTransitionAsync(job.Id, ScoringJobState.Running)).Should().BeTrue();
        (await store.TryTransitionAsync(job.Id, ScoringJobState.Completed)).Should().BeTrue();
        (await store.TryTransitionAsync(job.Id, ScoringJobState.Running)).Should().BeFalse("completed is terminal");
        (await store.TryTransitionAsync(job.Id, ScoringJobState.Queued)).Should().BeFalse("completed is terminal");

        (await db.ScoringJobs.SingleAsync(j => j.Id == job.Id)).State.Should().Be(ScoringJobState.Completed);
    }

    [Fact]
    public async Task Pause_sets_reason_and_resumeAt_and_leaving_pause_clears_them()
    {
        await using var db = NewDb();
        var store = Store(db);
        var job = await CreateJobAsync(store);
        await store.TryTransitionAsync(job.Id, ScoringJobState.Running);

        var resumeAt = Clock.GetUtcNow().AddHours(10);
        (await store.TryTransitionAsync(
            job.Id, ScoringJobState.Paused, ScoringJobPauseReason.Quota, resumeAt)).Should().BeTrue();
        var paused = await db.ScoringJobs.SingleAsync(j => j.Id == job.Id);
        paused.PauseReason.Should().Be("quota");
        paused.ResumeAt.Should().Be(resumeAt);

        (await store.TryTransitionAsync(job.Id, ScoringJobState.Queued)).Should().BeTrue("resume re-queues");
        var requeued = await db.ScoringJobs.SingleAsync(j => j.Id == job.Id);
        requeued.PauseReason.Should().BeNull();
        requeued.ResumeAt.Should().BeNull();
    }

    [Fact]
    public async Task Chunk_results_batch_write_and_ignore_unknown_employees()
    {
        await using var db = NewDb();
        var store = Store(db);
        var job = await CreateJobAsync(store);

        await store.WriteChunkResultsAsync(job.Id,
        [
            new ScoringCandidateResult(Ada, ScoringCandidateStatus.Scored, 82, "Strong", "Great fit.", true, null),
            new ScoringCandidateResult(Grace, ScoringCandidateStatus.Failed, null, null, null, null, "chunk fault"),
            new ScoringCandidateResult(Guid.NewGuid(), ScoringCandidateStatus.Scored, 99, "Strong", "??", true, null),
        ]);

        var rows = await db.ScoringJobCandidates.Where(c => c.JobId == job.Id).ToListAsync();
        rows.Should().HaveCount(2, "unknown employees are ignored, never inserted");
        rows.Single(r => r.EmployeeId == Ada).Score.Should().Be(82);
        rows.Single(r => r.EmployeeId == Grace).Error.Should().Be("chunk fault");
        ScoringJobProgress.Of(rows).Should().Be(new ScoringJobProgress(1, 1, 0, 2));
        ScoringJobProgress.Of(rows).Settled.Should().Be(2, "failed counts as settled — N/N like staffing");
    }

    [Fact]
    public async Task Resumable_sweep_finds_due_paused_queued_and_orphaned_running_only()
    {
        await using var db = NewDb();
        var store = Store(db);

        var duePaused = await CreateJobAsync(store);
        await store.TryTransitionAsync(duePaused.Id, ScoringJobState.Running);
        await store.TryTransitionAsync(duePaused.Id, ScoringJobState.Paused,
            ScoringJobPauseReason.Quota, Clock.GetUtcNow().AddHours(-1));

        var futurePaused = await CreateJobAsync(store);
        await store.TryTransitionAsync(futurePaused.Id, ScoringJobState.Running);
        await store.TryTransitionAsync(futurePaused.Id, ScoringJobState.Paused,
            ScoringJobPauseReason.Cap, Clock.GetUtcNow().AddHours(6));

        var orphanRunning = await CreateJobAsync(store);
        await store.TryTransitionAsync(orphanRunning.Id, ScoringJobState.Running);

        var queued = await CreateJobAsync(store);

        var done = await CreateJobAsync(store);
        await store.TryTransitionAsync(done.Id, ScoringJobState.Running);
        await store.TryTransitionAsync(done.Id, ScoringJobState.Completed);

        var resumable = (await store.FindResumableAsync()).Select(j => j.Id).ToList();

        resumable.Should().Contain([duePaused.Id, orphanRunning.Id, queued.Id])
            .And.NotContain(futurePaused.Id)
            .And.NotContain(done.Id);
    }

    [Fact]
    public async Task Get_orders_candidates_scored_by_score_then_failed_then_pending()
    {
        await using var db = NewDb();
        var store = Store(db);
        var extra = Guid.NewGuid();
        var job = await store.CreateAsync(null, "JD", null, null, 10,
        [
            new ScoringCandidateSeed(Ada, "Ada", "Engineer"),
            new ScoringCandidateSeed(Grace, "Grace", "Admiral"),
            new ScoringCandidateSeed(extra, "Pending Pat", "Engineer"),
        ]);
        await store.WriteChunkResultsAsync(job.Id,
        [
            new ScoringCandidateResult(Ada, ScoringCandidateStatus.Scored, 60, "Moderate", "ok", true, null),
            new ScoringCandidateResult(Grace, ScoringCandidateStatus.Scored, 90, "Strong", "great", true, null),
        ]);

        var loaded = await store.GetAsync(job.Id);

        loaded!.Candidates.Select(c => c.EmployeeId).Should().Equal(Grace, Ada, extra);
    }

    [Fact]
    public async Task List_scopes_to_the_requester_newest_first_without_candidates()
    {
        await using var db = NewDb();
        var store = Store(db);
        var user = Guid.NewGuid();
        var first = await store.CreateAsync(user, "JD 1", null, null, 10, [new ScoringCandidateSeed(Ada, "Ada", "E")]);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await store.CreateAsync(user, "JD 2", null, null, 10, [new ScoringCandidateSeed(Grace, "Grace", "E")]);
        await store.CreateAsync(Guid.NewGuid(), "Someone else's JD", null, null, 10, []);

        var jobs = await store.ListAsync(user);

        jobs.Select(j => j.Id).Should().Equal(second.Id, first.Id);
    }
}
