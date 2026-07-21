using System.Net;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Staffing;
using EmployeeManager.Agents.Tests.Fakes;
using EmployeeManager.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Unit tests for the staffing pipeline (Prepare → Shortlist → Match×N → Aggregate → Narrative →
/// Report) with every step core faked: the run services, the narrative chat client, and the usage
/// services. They pin the P1T-71 report semantics — deterministic fields from step outputs, the
/// narrative corruption guards, bounded match parallelism, cap re-checks, per-step metering, and
/// the never-throw partial-failure ladder.
/// </summary>
public class StaffingPipelineTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-00000000000{n}");

    private static ShortlistCandidateItem Candidate(int n) => new(
        Id(n),
        $"Person {n}",
        $"Title {n}",
        0.9 - (n * 0.05),
        new ShortlistCoverage(2, 3),
        [
            new ShortlistRequirementItem("event streaming with Kafka", true, "Built Kafka pipelines."),
            new ShortlistRequirementItem("Kubernetes operations", true, "Ran K8s clusters."),
            new ShortlistRequirementItem("team leadership", false, null),
        ],
        $"Shortlist rationale {n}");

    private static ShortlistRunOutcome ShortlistOk(params ShortlistCandidateItem[] candidates) => new(
        "shortlist",
        new AgentReply("[]", 100, 20, 120),
        new ShortlistResponse(["event streaming with Kafka", "Kubernetes operations", "team leadership"], candidates),
        FaultDetail: null);

    private static MatchRunOutcome MatchOk(Guid employeeId) => new(
        "match",
        $"Gap analysis for {employeeId}.\n\nOverall score: 78/100\nOverall band: Strong",
        new AgentReply("answer", 200, 50, 250));

    private static FakeMatchRunService MatchAlwaysOk() =>
        new((id, _) => Task.FromResult(MatchOk(id)));

    private static string NarrativeJson(Guid first, Guid second) =>
        $$"""
          {"rationales":[{"employeeId":"{{first}}","rationale":"R1"},{"employeeId":"{{second}}","rationale":"R2"}],
           "recommendation":{"employeeId":"{{first}}","narrative":"Pick person one."} }
          """;

    private static FakeChatClient NarrativeChat(string json) => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 15, TotalTokenCount = 45 },
        });

    private static StaffingPipeline Pipeline(
        IShortlistRunService shortlist,
        IMatchRunService match,
        IChatClient chat,
        IUsageService? usage = null,
        IUsageMeter? meter = null,
        int maxConcurrentMatches = 2,
        StaffingRetryPolicy? retry = null) => new(
        shortlist,
        match,
        chat,
        usage ?? new FakeUsageService(),
        meter ?? new RecordingUsageMeter(),
        new StaffingThrottle(maxConcurrentMatches),
        retry ?? new StaffingRetryPolicy(MaxAttempts: 3, _ => TimeSpan.Zero),
        NullLogger<StaffingPipeline>.Instance);

    private static Task<StaffingRunOutcome> RunAsync(
        StaffingPipeline pipeline, int? matchTop = null, string jobDescription = "Platform engineer.") =>
        pipeline.RunAsync(new StaffingPipelineRequest(jobDescription, MatchTop: matchTop), UserId);

    // ----- Happy path -----------------------------------------------------------------------

    [Fact]
    public async Task Happy_path_composes_the_full_report_from_step_outputs()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2)));
        var chat = NarrativeChat(NarrativeJson(Id(1), Id(2)));
        var pipeline = Pipeline(shortlist, MatchAlwaysOk(), chat);

        var outcome = await RunAsync(pipeline);

        outcome.ShortlistFault.Should().BeNull();
        outcome.Report.Should().NotBeNull();
        var report = outcome.Report!;

        report.Requirements.Should().Equal(
            "event streaming with Kafka", "Kubernetes operations", "team leadership");
        report.Candidates.Should().HaveCount(2);

        var first = report.Candidates[0];
        first.EmployeeId.Should().Be(Id(1));
        first.Name.Should().Be("Person 1");
        first.Title.Should().Be("Title 1");
        first.Shortlist.Score.Should().BeApproximately(0.85, 0.0001);
        first.Shortlist.Coverage.Should().Be(new ShortlistCoverage(2, 3));
        first.Shortlist.Requirements.Should().HaveCount(3);
        first.Match.Status.Should().Be("completed");
        first.Match.Score.Should().Be(78);
        first.Match.Band.Should().Be("Strong");
        first.Match.Answer.Should().Contain("Overall score: 78/100");
        first.Match.Error.Should().BeNull();
        first.Rationale.Should().Be("R1");
        report.Candidates[1].Rationale.Should().Be("R2");

        report.Recommendation.Should().NotBeNull();
        report.Recommendation!.EmployeeId.Should().Be(Id(1));
        report.Recommendation.Narrative.Should().Be("Pick person one.");
        report.Degraded.Should().BeFalse();
        report.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Happy_path_gives_the_narrative_call_the_assembled_evidence_without_tools()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2)));
        var chat = NarrativeChat(NarrativeJson(Id(1), Id(2)));
        var pipeline = Pipeline(shortlist, MatchAlwaysOk(), chat);

        await RunAsync(pipeline);

        chat.CallCount.Should().Be(1);
        var prompt = string.Concat(chat.ReceivedMessages[0].Select(m => m.Text));
        prompt.Should().Contain("Person 1").And.Contain("Person 2")
            .And.Contain(Id(1).ToString()).And.Contain("2/3");
        chat.ReceivedOptions[0]?.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Emits_ordered_progress_events_covering_the_pipeline_stages()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2)));
        var pipeline = Pipeline(shortlist, MatchAlwaysOk(), NarrativeChat(NarrativeJson(Id(1), Id(2))));

        var reported = new List<StaffingProgressEvent>();
        var progress = new Progress<StaffingProgressEvent>(reported.Add);
        var outcome = await pipeline.RunAsync(
            new StaffingPipelineRequest("Platform engineer."), UserId, progress);

        outcome.Events.Select(e => e.Sequence).Should().BeInAscendingOrder();
        outcome.Events.Select(e => e.Sequence).Should().OnlyHaveUniqueItems();
        outcome.Events.Select(e => e.Stage).Should().ContainInOrder(
            "prepare", "shortlist", "match", "aggregate", "narrative", "report");
    }

    [Fact]
    public async Task Emits_step_status_payloads_with_candidate_names_and_counters()
    {
        // maxConcurrentMatches: 1 serialises the fan-out so the per-candidate order is exact.
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2)));
        var pipeline = Pipeline(
            shortlist, MatchAlwaysOk(), NarrativeChat(NarrativeJson(Id(1), Id(2))), maxConcurrentMatches: 1);

        var outcome = await RunAsync(pipeline, matchTop: 2);

        outcome.Events
            .Where(e => e.Status is not null)
            .Select(e => (e.Stage, e.Status, e.CandidateName, e.CompletedCount, e.TotalCount))
            .Should().Equal(
                ("shortlist", "started", null, null, null),
                ("shortlist", "completed", null, null, null),
                ("match", "started", "Person 1", null, 2),
                ("match", "completed", "Person 1", 1, 2),
                ("match", "started", "Person 2", null, 2),
                ("match", "completed", "Person 2", 2, 2),
                ("narrative", "started", null, null, null),
                ("narrative", "completed", null, null, null));
        outcome.Events.Where(e => e.CandidateName is not null)
            .Should().OnlyContain(e => e.EmployeeId != null);
    }

    [Fact]
    public async Task A_failed_match_emits_a_failed_status_event_carrying_the_error()
    {
        var match = new FakeMatchRunService((_, _) => throw new InvalidOperationException("cv_get exploded"));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(1))));

        var outcome = await RunAsync(pipeline, matchTop: 1);

        var failed = outcome.Events.Should()
            .ContainSingle(e => e.Stage == "match" && e.Status == "failed").Subject;
        failed.CandidateName.Should().Be("Person 1");
        failed.Error.Should().Contain("cv_get exploded");
        failed.CompletedCount.Should().Be(1, "a failed run still advances the k/N progress counter");
        failed.TotalCount.Should().Be(1);
    }

    // ----- matchTop handling ----------------------------------------------------------------

    [Theory]
    [InlineData(null, 3)]
    [InlineData(0, 1)]
    [InlineData(10, 5)]
    [InlineData(4, 4)]
    public async Task MatchTop_defaults_to_3_and_clamps_to_1_through_5(int? matchTop, int expectedTopK)
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(1)));
        var pipeline = Pipeline(shortlist, MatchAlwaysOk(), NarrativeChat(NarrativeJson(Id(1), Id(1))));

        await RunAsync(pipeline, matchTop);

        shortlist.Requests.Should().ContainSingle().Which.TopK.Should().Be(expectedTopK);
    }

    [Fact]
    public async Task Only_the_top_matchTop_shortlist_candidates_feed_the_match_fan_out()
    {
        // Defensive: even if the shortlist step returns more than matchTop candidates, only the
        // top matchTop are matched and reported.
        var shortlist = new FakeShortlistRunService(
            ShortlistOk(Candidate(1), Candidate(2), Candidate(3), Candidate(4), Candidate(5)));
        var match = MatchAlwaysOk();
        var pipeline = Pipeline(shortlist, match, NarrativeChat(NarrativeJson(Id(1), Id(2))));

        var outcome = await RunAsync(pipeline, matchTop: 2);

        match.Calls.Should().Be(2);
        outcome.Report!.Candidates.Should().HaveCount(2);
        outcome.Report.Candidates.Select(c => c.EmployeeId).Should().Equal(Id(1), Id(2));
    }

    // ----- Narrative corruption guards ------------------------------------------------------

    [Fact]
    public async Task Narrative_rationales_with_unknown_employee_ids_are_dropped_for_templates()
    {
        var chat = NarrativeChat(
            $$"""
              {"rationales":[{"employeeId":"{{Guid.NewGuid()}}","rationale":"Bogus."}],
               "recommendation":{"employeeId":"{{Id(1)}}","narrative":"Pick person one."} }
              """);
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))), MatchAlwaysOk(), chat);

        var outcome = await RunAsync(pipeline);

        // Unknown-id rationales never reach the report; the affected candidates degrade to the
        // deterministic template, but the validated recommendation survives.
        outcome.Report!.Candidates.Should().OnlyContain(c => c.Rationale.Contains("Matched 2/3"));
        outcome.Report.Recommendation!.EmployeeId.Should().Be(Id(1));
    }

    [Fact]
    public async Task Recommendation_for_an_unknown_candidate_degrades_to_none()
    {
        var chat = NarrativeChat(
            $$"""
              {"rationales":[{"employeeId":"{{Id(1)}}","rationale":"R1"},{"employeeId":"{{Id(2)}}","rationale":"R2"}],
               "recommendation":{"employeeId":"{{Guid.NewGuid()}}","narrative":"Pick a ghost."} }
              """);
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))), MatchAlwaysOk(), chat);

        var outcome = await RunAsync(pipeline);

        outcome.Report!.Recommendation.Should().BeNull();
        outcome.Report.Degraded.Should().BeTrue();
        outcome.Report.Notes.Should().Contain(n => n.Contains("recommendation", StringComparison.OrdinalIgnoreCase));
        // The valid rationales still ship.
        outcome.Report.Candidates[0].Rationale.Should().Be("R1");
    }

    // ----- Throttle and retry ---------------------------------------------------------------

    [Fact]
    public async Task Concurrent_match_runs_never_exceed_the_configured_limit()
    {
        var current = 0;
        var maxObserved = 0;
        var match = new FakeMatchRunService(async (id, _) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref maxObserved, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref current);
            return MatchOk(id);
        });
        var shortlist = new FakeShortlistRunService(
            ShortlistOk(Candidate(1), Candidate(2), Candidate(3), Candidate(4), Candidate(5)));
        var pipeline = Pipeline(
            shortlist, match, NarrativeChat(NarrativeJson(Id(1), Id(2))), maxConcurrentMatches: 2);

        await RunAsync(pipeline, matchTop: 5);

        match.Calls.Should().Be(5);
        Volatile.Read(ref maxObserved).Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Match_rate_limit_faults_are_retried_until_they_succeed()
    {
        var attempts = 0;
        var match = new FakeMatchRunService((id, _) =>
            ++attempts < 3
                ? throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)
                : Task.FromResult(MatchOk(id)));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(1))),
            retry: new StaffingRetryPolicy(MaxAttempts: 3, _ => TimeSpan.Zero));

        var outcome = await RunAsync(pipeline, matchTop: 1);

        match.Calls.Should().Be(3);
        outcome.Report!.Candidates[0].Match.Status.Should().Be("completed");
        outcome.Report.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task Match_rate_limit_faults_exhaust_the_retry_budget_then_fail_the_candidate()
    {
        var match = new FakeMatchRunService((_, _) =>
            throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(1))),
            retry: new StaffingRetryPolicy(MaxAttempts: 3, _ => TimeSpan.Zero));

        var outcome = await RunAsync(pipeline, matchTop: 1);

        match.Calls.Should().Be(3);
        outcome.Report!.Candidates[0].Match.Status.Should().Be("failed");
        outcome.Report.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task Non_rate_limit_match_faults_are_not_retried()
    {
        var match = new FakeMatchRunService((_, _) => throw new InvalidOperationException("boom"));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(1))));

        var outcome = await RunAsync(pipeline, matchTop: 1);

        match.Calls.Should().Be(1);
        outcome.Report!.Candidates[0].Match.Status.Should().Be("failed");
        outcome.Report.Candidates[0].Match.Error.Should().Contain("boom");
    }

    // ----- Partial failure ladder -----------------------------------------------------------

    [Fact]
    public async Task Shortlist_soft_fault_yields_an_error_outcome_with_the_reply_still_metered()
    {
        var meter = new RecordingUsageMeter();
        var shortlist = new FakeShortlistRunService(new ShortlistRunOutcome(
            "shortlist", new AgentReply("[]", 100, 20, 120), Response: null,
            FaultDetail: "The semantic search backend is unavailable."));
        var match = MatchAlwaysOk();
        var chat = NarrativeChat("{}");
        var pipeline = Pipeline(shortlist, match, chat, meter: meter);

        var outcome = await RunAsync(pipeline);

        outcome.Report.Should().BeNull();
        outcome.ShortlistFault.Should().Contain("semantic search backend");
        match.Calls.Should().Be(0);
        chat.CallCount.Should().Be(0);
        meter.Records.Should().ContainSingle().Which.AgentName.Should().Be("shortlist");
    }

    [Fact]
    public async Task Shortlist_transport_fault_yields_an_error_outcome_without_throwing()
    {
        var shortlist = new FakeShortlistRunService(
            _ => throw new HttpRequestException("model endpoint unreachable"));
        var pipeline = Pipeline(shortlist, MatchAlwaysOk(), NarrativeChat("{}"));

        var outcome = await RunAsync(pipeline);

        outcome.Report.Should().BeNull();
        outcome.ShortlistFault.Should().Contain("model endpoint unreachable");
    }

    [Fact]
    public async Task One_failed_match_degrades_the_report_but_ships_the_rest()
    {
        var match = new FakeMatchRunService((id, _) =>
            id == Id(2)
                ? throw new InvalidOperationException("cv_get exploded")
                : Task.FromResult(MatchOk(id)));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(2))));

        var outcome = await RunAsync(pipeline);

        var report = outcome.Report!;
        report.Candidates[0].Match.Status.Should().Be("completed");
        var failed = report.Candidates[1].Match;
        failed.Status.Should().Be("failed");
        failed.Error.Should().Contain("cv_get exploded");
        failed.Answer.Should().BeNull();
        failed.Score.Should().BeNull();
        report.Degraded.Should().BeTrue();
        report.Notes.Should().Contain(n => n.Contains("Person 2"));
        // The narrative step still runs on the surviving evidence.
        report.Candidates[0].Rationale.Should().Be("R1");
    }

    [Fact]
    public async Task All_matches_failing_still_ships_a_shortlist_only_report()
    {
        var match = new FakeMatchRunService((_, _) => throw new InvalidOperationException("down"));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))),
            match,
            NarrativeChat(NarrativeJson(Id(1), Id(2))));

        var outcome = await RunAsync(pipeline);

        var report = outcome.Report!;
        report.Candidates.Should().HaveCount(2);
        report.Candidates.Should().OnlyContain(c => c.Match.Status == "failed");
        report.Candidates.Should().OnlyContain(c => c.Shortlist.Coverage.Total == 3);
        report.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task Narrative_failure_degrades_to_templated_rationales_without_a_recommendation()
    {
        var chat = new FakeChatClient(() => throw new HttpRequestException("model endpoint unreachable"));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))), MatchAlwaysOk(), chat);

        var outcome = await RunAsync(pipeline);

        var report = outcome.Report!;
        report.Candidates.Should().OnlyContain(c => c.Rationale.Contains("Matched 2/3"));
        report.Candidates.Should().OnlyContain(c => c.Match.Status == "completed");
        report.Recommendation.Should().BeNull();
        report.Degraded.Should().BeTrue();
        report.Notes.Should().Contain(n => n.Contains("narrative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unparseable_narrative_output_degrades_like_a_narrative_failure()
    {
        var chat = NarrativeChat("I think Person 1 is great!");
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))), MatchAlwaysOk(), chat);

        var outcome = await RunAsync(pipeline, matchTop: 1);

        outcome.Report!.Candidates[0].Rationale.Should().Contain("Matched 2/3");
        outcome.Report.Recommendation.Should().BeNull();
        outcome.Report.Degraded.Should().BeTrue();
    }

    // ----- Cap re-checks --------------------------------------------------------------------

    private static WindowUsage Exceeded() => new("daily", 50_000, 50_000, DateTimeOffset.UtcNow.AddHours(3));

    [Fact]
    public async Task Cap_trip_before_the_fan_out_skips_matches_and_narrative_with_a_cap_note()
    {
        var usage = new ScriptedUsageService(Exceeded());
        var match = MatchAlwaysOk();
        var chat = NarrativeChat(NarrativeJson(Id(1), Id(2)));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))), match, chat, usage: usage);

        var outcome = await RunAsync(pipeline);

        match.Calls.Should().Be(0);
        chat.CallCount.Should().Be(0);
        var report = outcome.Report!;
        report.Candidates.Should().OnlyContain(c => c.Match.Status == "skipped");
        report.Candidates.Should().OnlyContain(c => c.Rationale.Contains("Matched 2/3"));
        report.Recommendation.Should().BeNull();
        report.Degraded.Should().BeTrue();
        report.Notes.Should().Contain(n => n.Contains("cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cap_trip_before_the_narrative_ships_completed_matches_with_a_cap_note()
    {
        var usage = new ScriptedUsageService(null, Exceeded());
        var match = MatchAlwaysOk();
        var chat = NarrativeChat(NarrativeJson(Id(1), Id(2)));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))), match, chat, usage: usage);

        var outcome = await RunAsync(pipeline);

        match.Calls.Should().Be(2);
        chat.CallCount.Should().Be(0);
        var report = outcome.Report!;
        report.Candidates.Should().OnlyContain(c => c.Match.Status == "completed");
        report.Recommendation.Should().BeNull();
        report.Degraded.Should().BeTrue();
        report.Notes.Should().Contain(n => n.Contains("cap", StringComparison.OrdinalIgnoreCase));
    }

    // ----- Metering -------------------------------------------------------------------------

    [Fact]
    public async Task Meters_each_step_under_its_agent_name()
    {
        var meter = new RecordingUsageMeter();
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))),
            MatchAlwaysOk(),
            NarrativeChat(NarrativeJson(Id(1), Id(2))),
            meter: meter);

        await RunAsync(pipeline);

        meter.Records.Should().HaveCount(4);
        meter.Records.Select(r => r.AgentName).Should().Equal("shortlist", "match", "match", "staffing");
        meter.Records.Should().OnlyContain(r => r.UserId == UserId);
        meter.Records[^1].Reply.TotalTokens.Should().Be(45);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        while ((snapshot = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, snapshot) != snapshot)
        {
        }
    }
}
