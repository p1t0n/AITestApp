using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Staffing;
using EmployeeManager.Agents.Tests.Fakes;
using EmployeeManager.Agents.Usage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/staffing (SSE slice, P1T-76). They run against the real host
/// but swap the pipeline's step seams — the shortlist/match run services and the narrative chat
/// client — for fakes. The pre-checks (401/400/429) answer as plain HTTP before the stream opens;
/// everything after streams as the pinned SSE contract: step/stepFailed per stage transition,
/// then one terminal report (partial results ship degraded) or error frame.
/// </summary>
public class StaffingEndpointTests
{
    private static readonly Guid AdaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GraceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ShortlistCandidateItem Candidate(Guid id, string name) => new(
        id,
        name,
        "Platform Lead",
        0.91,
        new ShortlistCoverage(2, 3),
        [
            new ShortlistRequirementItem("event streaming with Kafka", true, "Built Kafka pipelines."),
            new ShortlistRequirementItem("Kubernetes operations", true, "Ran K8s clusters."),
            new ShortlistRequirementItem("team leadership", false, null),
        ],
        "Strong Kafka and K8s evidence.");

    private static ShortlistRunOutcome ShortlistOutcome() => new(
        "shortlist",
        new AgentReply("[]", 100, 20, 120),
        new ShortlistResponse(
            ["event streaming with Kafka", "Kubernetes operations", "team leadership"],
            [Candidate(AdaId, "Ada Lovelace"), Candidate(GraceId, "Grace Hopper")]),
        FaultDetail: null);

    private static FakeShortlistRunService ShortlistOk() => new(ShortlistOutcome());

    private static FakeMatchRunService MatchOk() => new((id, _) => Task.FromResult(new MatchRunOutcome(
        "match",
        $"Gap analysis for {id}.\n\nOverall score: 78/100\nOverall band: Strong",
        new AgentReply("answer", 200, 50, 250))));

    private static FakeChatClient NarrativeChat() => new(() => new ChatResponse(new ChatMessage(
        ChatRole.Assistant,
        $$"""
          {"rationales":[{"employeeId":"{{AdaId}}","rationale":"Best coverage."},{"employeeId":"{{GraceId}}","rationale":"Solid depth."}],
           "recommendation":{"employeeId":"{{AdaId}}","narrative":"Ada is the strongest fit."} }
          """))
    {
        Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 15, TotalTokenCount = 45 },
    });

    private static WebApplicationFactory<Program> FakedHost(
        IShortlistRunService? shortlist = null,
        IMatchRunService? match = null,
        IChatClient? chat = null,
        Action<IServiceCollection>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton(shortlist ?? ShortlistOk());
                s.AddSingleton(match ?? MatchOk());
                s.AddSingleton(chat ?? NarrativeChat());
                // One match lane: the fan-out runs serially, so event order is exact.
                s.AddSingleton(new StaffingThrottle(1));
                extra?.Invoke(s);
            }));

    /// <summary>The step-payload fields the SSE contract pins, as one comparable tuple.</summary>
    private static (string Stage, string Status, string? Candidate, int? Completed, int? Total) Step(SseFrame frame)
    {
        var data = frame.Json;
        return (
            data.GetProperty("stage").GetString()!,
            data.GetProperty("status").GetString()!,
            data.TryGetProperty("candidate", out var c) ? c.GetProperty("name").GetString() : null,
            data.TryGetProperty("completedCount", out var k) ? k.GetInt32() : null,
            data.TryGetProperty("totalCount", out var n) ? n.GetInt32() : null);
    }

    [Fact]
    public async Task Streams_ordered_step_events_then_the_terminal_report()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing",
            new { jobDescription = "Platform engineer: Kafka, Kubernetes, leadership.", matchTop = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var frames = await response.ReadAllSseFramesAsync();
        frames.Select(f => f.Event).Should().Equal(
            "step", "step", "step", "step", "step", "step", "step", "step", "report");
        frames.Take(8).Select(Step).Should().Equal(
            ("shortlist", "started", null, null, null),
            ("shortlist", "completed", null, null, null),
            ("match", "started", "Ada Lovelace", null, 2),
            ("match", "completed", "Ada Lovelace", 1, 2),
            ("match", "started", "Grace Hopper", null, 2),
            ("match", "completed", "Grace Hopper", 2, 2),
            ("narrative", "started", null, null, null),
            ("narrative", "completed", null, null, null));

        // Candidates carry both id and name; optional fields are omitted, not null.
        frames[2].Json.GetProperty("candidate").GetProperty("employeeId").GetString()
            .Should().Be(AdaId.ToString());
        frames[0].Json.TryGetProperty("candidate", out _).Should().BeFalse();
        frames[0].Json.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Returns_400_when_the_job_description_is_blank()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_429_when_a_usage_cap_is_already_exceeded()
    {
        var exceeded = new WindowUsage("daily", 50_000, 50_000, DateTimeOffset.UtcNow.AddHours(3));
        var shortlist = ShortlistOk();
        using var factory = FakedHost(shortlist,
            extra: s => s.AddSingleton<IUsageService>(new FakeUsageService(exceeded)));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be("daily");
        shortlist.Requests.Should().BeEmpty("the pre-check must stop the pipeline before any step runs");
    }

    [Fact]
    public async Task A_shortlist_fault_ends_the_stream_with_a_terminal_error_and_no_report()
    {
        var shortlist = new FakeShortlistRunService(
            _ => throw new HttpRequestException("model endpoint unreachable"));
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the fault happens after the stream opened");
        var frames = await response.ReadAllSseFramesAsync();
        frames.Select(f => f.Event).Should().Equal(
            new[] { "step", "error" }, "an unrecoverable shortlist failure is a terminal error, not a stepFailed");
        Step(frames[0]).Should().Be(("shortlist", "started", null, null, null));
        var error = frames[^1].Json;
        error.GetProperty("title").GetString().Should().Contain("shortlist");
        error.GetProperty("detail").GetString().Should().Contain("model endpoint unreachable");
    }

    [Fact]
    public async Task A_shortlist_soft_fault_ends_the_stream_with_a_terminal_error()
    {
        var shortlist = new FakeShortlistRunService(new ShortlistRunOutcome(
            "shortlist", new AgentReply("[]", 100, 20, 120), Response: null,
            FaultDetail: "The semantic search backend is unavailable."));
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        var frames = await response.ReadAllSseFramesAsync();
        frames[^1].Event.Should().Be("error");
        frames[^1].Json.GetProperty("detail").GetString().Should().Contain("semantic search backend");
        frames.Should().NotContain(f => f.Event == "report");
    }

    [Fact]
    public async Task The_terminal_report_event_carries_the_pinned_camel_case_report_contract()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing",
            new { jobDescription = "Platform engineer: Kafka, Kubernetes, leadership.", matchTop = 2 });

        response.EnsureSuccessStatusCode();
        var frames = await response.ReadAllSseFramesAsync();
        frames[^1].Event.Should().Be("report");
        var body = frames[^1].Json;

        body.GetProperty("requirements").GetArrayLength().Should().Be(3);
        body.GetProperty("requirements")[0].GetString().Should().Be("event streaming with Kafka");

        var candidates = body.GetProperty("candidates");
        candidates.GetArrayLength().Should().Be(2);
        var ada = candidates[0];
        ada.GetProperty("employeeId").GetString().Should().Be(AdaId.ToString());
        ada.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        ada.GetProperty("title").GetString().Should().Be("Platform Lead");

        var shortlist = ada.GetProperty("shortlist");
        shortlist.GetProperty("score").GetDouble().Should().BeApproximately(0.91, 0.0001);
        shortlist.GetProperty("coverage").GetProperty("matched").GetInt32().Should().Be(2);
        shortlist.GetProperty("coverage").GetProperty("total").GetInt32().Should().Be(3);
        var perRequirement = shortlist.GetProperty("requirements");
        perRequirement.GetArrayLength().Should().Be(3);
        perRequirement[0].GetProperty("text").GetString().Should().Be("event streaming with Kafka");
        perRequirement[0].GetProperty("matched").GetBoolean().Should().BeTrue();
        perRequirement[0].GetProperty("snippet").GetString().Should().Be("Built Kafka pipelines.");
        perRequirement[2].TryGetProperty("snippet", out _).Should().BeFalse("null snippets are omitted");

        var match = ada.GetProperty("match");
        match.GetProperty("status").GetString().Should().Be("completed");
        match.GetProperty("score").GetInt32().Should().Be(78);
        match.GetProperty("band").GetString().Should().Be("Strong");
        match.GetProperty("answer").GetString().Should().Contain("Overall score: 78/100");
        match.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null, "error is pinned as an explicit null");

        ada.GetProperty("rationale").GetString().Should().Be("Best coverage.");

        var recommendation = body.GetProperty("recommendation");
        recommendation.GetProperty("employeeId").GetString().Should().Be(AdaId.ToString());
        recommendation.GetProperty("narrative").GetString().Should().Be("Ada is the strongest fit.");

        body.GetProperty("degraded").GetBoolean().Should().BeFalse();
        body.GetProperty("notes").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task One_failed_match_streams_a_stepFailed_then_a_degraded_report()
    {
        var match = new FakeMatchRunService((id, _) =>
            id == GraceId
                ? throw new InvalidOperationException("cv_get exploded")
                : Task.FromResult(new MatchRunOutcome(
                    "match", "Overall score: 78/100\nOverall band: Strong", new AgentReply("a", 200, 50, 250))));
        using var factory = FakedHost(match: match);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "partial results ship as a degraded report, never an error");
        var frames = await response.ReadAllSseFramesAsync();

        var stepFailed = frames.Should().ContainSingle(f => f.Event == "stepFailed").Subject;
        Step(stepFailed).Should().Be(("match", "failed", "Grace Hopper", 2, 2));
        stepFailed.Json.GetProperty("error").GetString().Should().Contain("cv_get exploded");

        frames[^1].Event.Should().Be("report");
        var body = frames[^1].Json;
        body.GetProperty("degraded").GetBoolean().Should().BeTrue();
        var failed = body.GetProperty("candidates")[1].GetProperty("match");
        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("error").GetString().Should().Contain("cv_get exploded");
        failed.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("notes").EnumerateArray().Should().Contain(
            n => n.GetString()!.Contains("Grace Hopper"));
    }

    [Fact]
    public async Task All_matches_failing_stream_a_stepFailed_each_then_a_shortlist_only_report()
    {
        var match = new FakeMatchRunService((_, _) => throw new InvalidOperationException("down"));
        using var factory = FakedHost(match: match);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        var frames = await response.ReadAllSseFramesAsync();
        frames.Count(f => f.Event == "stepFailed" && f.Json.GetProperty("stage").GetString() == "match")
            .Should().Be(2);
        frames[^1].Event.Should().Be("report");
        var body = frames[^1].Json;
        body.GetProperty("candidates").EnumerateArray().Should().OnlyContain(
            c => c.GetProperty("match").GetProperty("status").GetString() == "failed");
        body.GetProperty("candidates").GetArrayLength().Should().Be(2, "the shortlist facts still ship");
        body.GetProperty("degraded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_narrative_failure_streams_a_stepFailed_then_a_degraded_report()
    {
        var chat = new FakeChatClient(() => throw new HttpRequestException("model endpoint unreachable"));
        using var factory = FakedHost(chat: chat);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        var frames = await response.ReadAllSseFramesAsync();
        var stepFailed = frames.Should().ContainSingle(f => f.Event == "stepFailed").Subject;
        stepFailed.Json.GetProperty("stage").GetString().Should().Be("narrative");
        stepFailed.Json.GetProperty("error").GetString().Should().Contain("model endpoint unreachable");

        frames[^1].Event.Should().Be("report");
        frames[^1].Json.GetProperty("degraded").GetBoolean().Should().BeTrue();
        frames[^1].Json.GetProperty("recommendation").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_cap_trip_mid_run_skips_the_remaining_step_events_and_reports_skipped_statuses()
    {
        // The scripted verdict trips the cap at the re-check after the shortlist step.
        var exceeded = new WindowUsage("daily", 50_000, 50_000, DateTimeOffset.UtcNow.AddHours(3));
        using var factory = FakedHost(
            extra: s => s.AddSingleton<IUsageService>(new ScriptedUsageService(null, exceeded)));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the pre-check passed; the trip happened mid-run");
        var frames = await response.ReadAllSseFramesAsync();
        frames.Select(f => (f.Event, Stage: f.Json.TryGetProperty("stage", out var s) ? s.GetString() : null))
            .Should().Equal(
                ("step", "shortlist"),
                ("step", "shortlist"),
                ("report", null));

        var body = frames[^1].Json;
        body.GetProperty("candidates").EnumerateArray().Should().OnlyContain(
            c => c.GetProperty("match").GetProperty("status").GetString() == "skipped");
        body.GetProperty("degraded").GetBoolean().Should().BeTrue();
        body.GetProperty("notes").EnumerateArray().Should().Contain(
            n => n.GetString()!.Contains("cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Returns_401_when_the_caller_is_unauthenticated()
    {
        using var factory = FakedHost();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Events_stream_incrementally_while_the_run_is_still_in_flight()
    {
        // The shortlist fake blocks on a gate the test only releases AFTER the first SSE frame
        // has been read — so that frame provably arrived while the pipeline was mid-step.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shortlist = new FakeShortlistRunService(async _ =>
        {
            await gate.Task;
            return ShortlistOutcome();
        });
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var first = await reader.ReadSseFrameAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Step(first!).Should().Be(("shortlist", "started", null, null, null));
        gate.Task.IsCompleted.Should().BeFalse("the first event arrived before the shortlist step finished");

        gate.SetResult();
        var rest = new List<SseFrame>();
        while (await reader.ReadSseFrameAsync().WaitAsync(TimeSpan.FromSeconds(10)) is { } frame)
        {
            rest.Add(frame);
        }

        rest[^1].Event.Should().Be("report");
    }

    [Fact]
    public async Task Client_disconnect_cancels_the_in_flight_pipeline_run()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shortlist = new FakeShortlistRunService(async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable: the delay only ends by cancellation");
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        using var cts = new CancellationTokenSource();
        var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." }, cts.Token);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var first = await reader.ReadSseFrameAsync().WaitAsync(TimeSpan.FromSeconds(10));
        first!.Event.Should().Be("step", "the run must be in flight before we disconnect");

        cts.Cancel();
        response.Dispose();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Emits_keep_alive_comments_while_a_step_is_slow()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shortlist = new FakeShortlistRunService(async _ =>
        {
            await gate.Task;
            return ShortlistOutcome();
        });
        using var factory = FakedHost(shortlist,
            extra: s => s.PostConfigure<StaffingOptions>(o => o.SseKeepAliveSeconds = 0.02));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var sawKeepAlive = false;
        while (!sawKeepAlive
               && await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) is { } line)
        {
            sawKeepAlive = line == ": ka";
        }

        sawKeepAlive.Should().BeTrue("an idle stream must carry keep-alive comments");
        gate.SetResult();
    }

    [Fact]
    public async Task Records_usage_under_the_step_agent_names()
    {
        var meter = new RecordingUsageMeter();
        using var factory = FakedHost(extra: s => s.AddSingleton<IUsageMeter>(meter));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer." });

        (await response.ReadAllSseFramesAsync())[^1].Event.Should().Be("report");
        meter.Records.Select(r => r.AgentName).Should().Equal("shortlist", "match", "match", "staffing");
    }
}
