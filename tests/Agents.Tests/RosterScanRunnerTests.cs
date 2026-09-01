using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Agents.Usage;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// RosterScanRunner semantics (P1T-124) over fakes: idempotent intake (one extraction, one
/// digest sweep), incremental chunk scoring, the pause ladder (quota → midnight Pacific, cap →
/// the window's reset), resume without re-scoring, honest terminal states, and per-agent
/// metering.
/// </summary>
public class RosterScanRunnerTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Linus = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class FakeExtractor(JdExtractionOutcome outcome) : IJdRequirementExtractor
    {
        public int Calls { get; private set; }

        public Task<JdExtractionOutcome> ExtractAsync(string jobDescription, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeDigests(params EmployeeDigestPage[] pages) : IRosterDigestSource
    {
        public int Calls { get; private set; }

        public Task<EmployeeDigestPage?> ListAsync(int page, int pageSize, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<EmployeeDigestPage?>(
                page <= pages.Length ? pages[page - 1] : new EmployeeDigestPage(page, pageSize, 0, []));
        }
    }

    private sealed class FakeTransport(Func<IReadOnlyList<EmployeeDigest>, ScoredChunk> score) : IScoringTransport
    {
        public int Calls { get; private set; }
        public List<int> ChunkSizes { get; } = [];

        public Task<ScoredChunk> ScoreChunkAsync(
            string jobDescription, JdRequirements? extraction, IReadOnlyList<EmployeeDigest> chunk,
            CancellationToken ct = default)
        {
            Calls++;
            ChunkSizes.Add(chunk.Count);
            return Task.FromResult(score(chunk));
        }
    }

    private sealed class ScriptedUsage(params WindowUsage?[] verdicts) : IUsageService
    {
        private readonly Queue<WindowUsage?> _verdicts = new(verdicts);

        public Task<UsageSnapshot> GetSnapshotAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<WindowUsage?> FindExceededAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_verdicts.Count > 0 ? _verdicts.Dequeue() : null);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"roster-scan-{Guid.NewGuid()}")
            .Options);

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    private static JdExtractionOutcome ExtractionOk() => new(
        "jd-extraction", new AgentReply("{}", 90, 30, 120),
        new JdRequirements([], JdSeniority.Unspecified, null, []), FaultDetail: null);

    private static EmployeeDigestPage Page(int page, int total, params (Guid Id, string Name)[] people) => new(
        page, 2, total,
        people.Select(p => new EmployeeDigest(p.Id, p.Name, "Engineer", $"Digest of {p.Name}")).ToList());

    private static ScoredChunk AllScored(IReadOnlyList<EmployeeDigest> chunk) => new(
        chunk.Select(c => new ScoringCandidateResult(
            c.EmployeeId, ScoringCandidateStatus.Scored, 75, "Strong", "fit", true, null)).ToList(),
        new AgentReply("{}", 200, 50, 250));

    private static (RosterScanRunner Runner, ScoringJobStore Store, FakeExtractor Extractor,
        FakeTransport Transport, RecordingUsageMeter Meter) Build(
            AppDbContext db,
            FakeTransport? transport = null,
            IUsageService? usage = null,
            FakeDigests? digests = null,
            FakeExtractor? extractor = null)
    {
        var store = new ScoringJobStore(db, Clock);
        var fakeExtractor = extractor ?? new FakeExtractor(ExtractionOk());
        var fakeTransport = transport ?? new FakeTransport(AllScored);
        var meter = new RecordingUsageMeter();
        var runner = new RosterScanRunner(
            store,
            fakeExtractor,
            digests ?? new FakeDigests(Page(1, 3, (Ada, "Ada"), (Grace, "Grace")), Page(2, 3, (Linus, "Linus"))),
            fakeTransport,
            new EmployeeFilterService(db),
            meter,
            usage ?? new ScriptedUsage(),
            new RosterScanOptions { ChunkSize = 2 },
            Clock,
            NullLogger<RosterScanRunner>.Instance);
        return (runner, store, fakeExtractor, fakeTransport, meter);
    }

    private static Task<ScoringJob> FreshJobAsync(ScoringJobStore store) =>
        store.CreateAsync(User, "Senior engineer JD", null, null, 2, []);

    [Fact]
    public async Task Fresh_job_intakes_once_scores_in_chunks_and_completes()
    {
        await using var db = NewDb();
        var (runner, store, extractor, transport, _) = Build(db);
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Completed);
        extractor.Calls.Should().Be(1);
        transport.Calls.Should().Be(2, "3 candidates in chunks of 2");
        transport.ChunkSizes.Should().Equal(2, 1);

        var settled = await store.GetAsync(job.Id);
        settled!.State.Should().Be(ScoringJobState.Completed);
        settled.ExtractionJson.Should().NotBeNullOrEmpty("the intake extraction persists on the job");
        settled.Candidates.Should().HaveCount(3).And.OnlyContain(c => c.Status == ScoringCandidateStatus.Scored);
        settled.Candidates.Should().OnlyContain(c => c.Digest.StartsWith("Digest of"));
    }

    [Fact]
    public async Task Meters_the_extraction_once_and_every_chunk_under_roster_scan()
    {
        await using var db = NewDb();
        var (runner, store, _, _, meter) = Build(db);

        await runner.RunAsync(await FreshJobAsync(store));

        meter.Records.Select(r => r.AgentName).Should().Equal("jd-extraction", "roster-scan", "roster-scan");
        meter.Records.Should().OnlyContain(r => r.UserId == User);
    }

    [Fact]
    public async Task Quota_exhaustion_pauses_until_the_next_pacific_midnight()
    {
        await using var db = NewDb();
        var transport = new FakeTransport(_ => throw new ScoringQuotaExceededException("quota"));
        var (runner, store, _, _, _) = Build(db, transport);
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Paused);
        var paused = await store.GetAsync(job.Id);
        paused!.State.Should().Be(ScoringJobState.Paused);
        paused.PauseReason.Should().Be("quota");
        paused.ResumeAt.Should().Be(RosterScanRunner.NextQuotaReset(Clock.GetUtcNow()));
        // 2026-08-16 12:00 UTC = 05:00 Pacific (PDT, UTC-7) → next midnight Pacific = 07:00 UTC next day.
        paused.ResumeAt.Should().Be(new DateTimeOffset(2026, 8, 17, 7, 0, 0, TimeSpan.Zero));
        paused.Candidates.Should().OnlyContain(c => c.Status == ScoringCandidateStatus.Pending,
            "nothing settled before the quota hit");
    }

    [Fact]
    public async Task Cap_trip_pauses_until_the_windows_reset_before_spending_a_chunk()
    {
        await using var db = NewDb();
        var resetAt = Clock.GetUtcNow().AddHours(3);
        var (runner, store, _, transport, _) = Build(
            db, usage: new ScriptedUsage(new WindowUsage("daily", 50000, 50000, resetAt)));
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Paused);
        var paused = await store.GetAsync(job.Id);
        paused!.PauseReason.Should().Be("cap");
        paused.ResumeAt.Should().Be(resetAt);
        transport.Calls.Should().Be(0, "the cap check runs before the chunk spends tokens");
    }

    [Fact]
    public async Task Resume_scores_only_pending_rows_and_reuses_the_persisted_extraction()
    {
        await using var db = NewDb();

        // First pass: cap trips after the first chunk settles.
        var firstPass = Build(db, usage: new ScriptedUsage(
            null, new WindowUsage("daily", 50000, 50000, Clock.GetUtcNow().AddHours(1))));
        var job = await FreshJobAsync(firstPass.Store);
        (await firstPass.Runner.RunAsync(job)).Should().Be(RosterScanRunResult.Paused);
        firstPass.Transport.Calls.Should().Be(1);

        // Resume pass (fresh scope): re-queued, then run again with the cap clear.
        (await firstPass.Store.TryTransitionAsync(job.Id, ScoringJobState.Queued)).Should().BeTrue();
        var resumed = await firstPass.Store.GetAsync(job.Id);
        var secondPass = Build(db);
        (await secondPass.Runner.RunAsync(resumed!)).Should().Be(RosterScanRunResult.Completed);

        secondPass.Extractor.Calls.Should().Be(0, "the persisted extraction is reused");
        secondPass.Transport.Calls.Should().Be(1, "only the remaining pending chunk scores");
        var done = await secondPass.Store.GetAsync(job.Id);
        done!.Candidates.Should().OnlyContain(c => c.Status == ScoringCandidateStatus.Scored);
    }

    [Fact]
    public async Task Extraction_fault_fails_the_job_with_detail()
    {
        await using var db = NewDb();
        var extractor = new FakeExtractor(new JdExtractionOutcome(
            "jd-extraction", new AgentReply("essay", 10, 5, 15), null, "did not parse"));
        var (runner, store, _, _, meter) = Build(db, extractor: extractor);
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Failed);
        var failed = await store.GetAsync(job.Id);
        failed!.State.Should().Be(ScoringJobState.Failed);
        failed.FailureDetail.Should().Contain("did not parse");
        meter.Records.Should().ContainSingle(r => r.AgentName == "jd-extraction",
            "tokens were spent either way");
    }

    [Fact]
    public async Task Per_candidate_failures_never_fail_the_job()
    {
        await using var db = NewDb();
        var transport = new FakeTransport(chunk => new ScoredChunk(
            chunk.Select((c, i) => i == 0
                ? new ScoringCandidateResult(c.EmployeeId, ScoringCandidateStatus.Failed, null, null, null, null, "boom")
                : new ScoringCandidateResult(c.EmployeeId, ScoringCandidateStatus.Scored, 60, "Moderate", "ok", true, null))
                .ToList(),
            new AgentReply("{}", 10, 5, 15)));
        var (runner, store, _, _, _) = Build(db, transport);
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Completed);
        var done = await store.GetAsync(job.Id);
        done!.State.Should().Be(ScoringJobState.Completed);
        done.Candidates.Should().Contain(c => c.Status == ScoringCandidateStatus.Failed)
            .And.Contain(c => c.Status == ScoringCandidateStatus.Scored);
    }

    [Fact]
    public async Task Non_quota_chunk_fault_fails_the_job()
    {
        await using var db = NewDb();
        var transport = new FakeTransport(_ => throw new HttpRequestException("model endpoint down"));
        var (runner, store, _, _, _) = Build(db, transport);
        var job = await FreshJobAsync(store);

        var result = await runner.RunAsync(job);

        result.Should().Be(RosterScanRunResult.Failed);
        (await store.GetAsync(job.Id))!.FailureDetail.Should().Contain("model endpoint down");
    }
}
