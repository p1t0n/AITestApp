using System.Net;
using System.Text.Json;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Handoff;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The handoff package accumulated by the staffing pipeline (P1T-132): one honest StageSlice per
/// unit of agent work, provenance instead of credentials, and DegradationEntries mirroring the
/// report's notes. Persistence is the next slice — here the package rides the in-memory outcome.
/// </summary>
public class StaffingHandoffPackageTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-00000000000{n}");

    private static ShortlistCandidateItem Candidate(int n) => new(
        Id(n),
        $"Person {n}",
        $"Title {n}",
        0.9 - (n * 0.05),
        new ShortlistCoverage(2, 3),
        [new ShortlistRequirementItem("Kafka", true, "Built Kafka pipelines.")],
        $"Shortlist rationale {n}");

    private static ShortlistRunOutcome ShortlistOk(params ShortlistCandidateItem[] candidates) => new(
        "shortlist",
        new AgentReply("[]", 100, 20, 120, ModelId: "gemini-3.5-flash-lite"),
        new ShortlistResponse(["Kafka"], candidates),
        FaultDetail: null);

    private static MatchRunOutcome MatchOk(Guid expertId) => new(
        "match",
        $"Gap analysis for {expertId}.",
        new AgentReply("answer", 200, 50, 250),
        Score: 78,
        Band: "Strong");

    private static FakeChatClient NarrativeChat(Guid first, Guid second) => new(
        () => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            $$"""
              {"rationales":[{"expertId":"{{first}}","rationale":"R1"},{"expertId":"{{second}}","rationale":"R2"}],
               "recommendation":{"expertId":"{{first}}","narrative":"Pick person one."} }
              """))
        {
            Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 15, TotalTokenCount = 45 },
        });

    /// <summary>The identities the McpAuth config would provide: the two MCP-backed agents have
    /// client ids and scopes; jd-extraction and the narrative are tool-less (absent on purpose).</summary>
    private sealed class MapIdentitySource : IAgentIdentitySource
    {
        public AgentIdentity? Find(string agentName) => agentName switch
        {
            "shortlist" => new AgentIdentity("agent-shortlist", ["mcp:read", "mcp:search"]),
            "match" => new AgentIdentity("agent-match", ["mcp:read"]),
            _ => null,
        };
    }

    private static StaffingPipeline Pipeline(
        IShortlistRunService shortlist,
        IMatchRunService match,
        IChatClient chat,
        IUsageService? usage = null,
        int maxConcurrentMatches = 1,
        StaffingRetryPolicy? retry = null) => new(
        shortlist,
        match,
        chat,
        usage ?? new FakeUsageService(),
        new RecordingUsageMeter(),
        new StaffingThrottle(maxConcurrentMatches),
        retry ?? new StaffingRetryPolicy(MaxAttempts: 3, _ => TimeSpan.Zero),
        new MapIdentitySource(),
        TimeProvider.System,
        NullLogger<StaffingPipeline>.Instance);

    private static Task<StaffingRunOutcome> RunAsync(StaffingPipeline pipeline, Guid? userId) =>
        pipeline.RunAsync(new StaffingPipelineRequest("Platform engineer.", MatchTop: 2), userId);

    // ----- Happy path -------------------------------------------------------------------------

    [Fact]
    public async Task Happy_run_yields_one_completed_slice_per_stage_with_tokens_scopes_and_timestamps()
    {
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(2)));

        var outcome = await RunAsync(pipeline, UserId);

        var package = outcome.Package;
        package.Slices.Select(s => s.Stage).Should().Equal("shortlist", "match", "match", "narrative");
        package.Slices.Should().OnlyContain(s => s.Status == StageSliceStatus.Completed);
        package.Degradations.Should().BeEmpty();

        var shortlist = package.Slices[0];
        shortlist.AgentClientId.Should().Be("agent-shortlist");
        shortlist.Scopes.Should().Equal("mcp:read", "mcp:search");
        shortlist.ModelId.Should().Be("gemini-3.5-flash-lite");
        shortlist.InputTokens.Should().Be(100);
        shortlist.OutputTokens.Should().Be(20);

        var match = package.Slices[1];
        match.AgentClientId.Should().Be("agent-match");
        match.Scopes.Should().Equal("mcp:read");
        match.InputTokens.Should().Be(200);
        match.OutputTokens.Should().Be(50);
        match.RetryCount.Should().Be(0);

        var narrative = package.Slices[3];
        narrative.AgentClientId.Should().BeNull("the narrative is a tool-less chat call with no MCP identity");
        narrative.Scopes.Should().BeEmpty();
        narrative.InputTokens.Should().Be(30);
        narrative.OutputTokens.Should().Be(15);

        package.Slices.Should().OnlyContain(s => s.StartedAt != default && s.CompletedAt >= s.StartedAt);

        package.Inputs["jobDescription"].Should().Be("Platform engineer.");
        package.Inputs["matchTop"].Should().Be("2");
        package.Provenance.CallerUserId.Should().Be(UserId);
        package.Provenance.StartedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task An_extraction_reply_adds_its_own_toolless_slice()
    {
        var outcome = ShortlistOk(Candidate(1));
        var shortlist = new FakeShortlistRunService(outcome with
        {
            ExtractionReply = new AgentReply("{}", 40, 10, 50),
        });
        var pipeline = Pipeline(
            shortlist,
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(1)));

        var run = await RunAsync(pipeline, UserId);

        var extraction = run.Package.Slices.Should()
            .ContainSingle(s => s.Stage == "jd-extraction").Subject;
        extraction.Status.Should().Be(StageSliceStatus.Completed);
        extraction.AgentClientId.Should().BeNull();
        extraction.Scopes.Should().BeEmpty();
        extraction.InputTokens.Should().Be(40);
        extraction.OutputTokens.Should().Be(10);
    }

    // ----- Provenance -------------------------------------------------------------------------

    [Fact]
    public async Task The_caps_snapshot_at_start_captures_all_three_windows()
    {
        var reset = DateTimeOffset.UtcNow.AddHours(1);
        var usage = new FakeUsageService(snapshot: new UsageSnapshot(
            new WindowUsage("daily", 100, 50_000, reset),
            new WindowUsage("weekly", 200, 200_000, reset),
            new WindowUsage("monthly", 300, 500_000, reset),
            []));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(1)),
            usage: usage);

        var outcome = await RunAsync(pipeline, UserId);

        outcome.Package.Provenance.CapsSnapshotAtStart.Should().Equal(
            new CapWindowSnapshot("daily", 100, 50_000, reset),
            new CapWindowSnapshot("weekly", 200, 200_000, reset),
            new CapWindowSnapshot("monthly", 300, 500_000, reset));
    }

    [Fact]
    public async Task An_unreadable_usage_snapshot_degrades_to_an_empty_caps_snapshot_not_a_failed_run()
    {
        // The default FakeUsageService throws from GetSnapshotAsync — the snapshot is fail-open.
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(1)));

        var outcome = await RunAsync(pipeline, UserId);

        outcome.Report.Should().NotBeNull();
        outcome.Package.Provenance.CapsSnapshotAtStart.Should().BeEmpty();
    }

    [Fact]
    public async Task An_anonymous_run_has_no_caller_and_no_caps_snapshot()
    {
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(1)));

        var outcome = await RunAsync(pipeline, userId: null);

        outcome.Package.Provenance.CallerUserId.Should().BeNull();
        outcome.Package.Provenance.CapsSnapshotAtStart.Should().BeEmpty();
    }

    // ----- Failure honesty ----------------------------------------------------------------------

    [Fact]
    public async Task A_failed_match_run_yields_a_failed_slice_with_its_retry_count_and_a_degradation_entry()
    {
        var match = new FakeMatchRunService((_, _) =>
            throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(Id(1), Id(1)),
            retry: new StaffingRetryPolicy(MaxAttempts: 3, _ => TimeSpan.Zero));

        var outcome = await RunAsync(pipeline, UserId);

        var slice = outcome.Package.Slices.Should().ContainSingle(s => s.Stage == "match").Subject;
        slice.Status.Should().Be(StageSliceStatus.Failed);
        slice.RetryCount.Should().Be(2, "three attempts mean two retries were performed");
        slice.DegradeReason.Should().Contain("rate limited");
        slice.InputTokens.Should().Be(0);

        var entry = outcome.Package.Degradations.Should()
            .ContainSingle(d => d.Stage == "match").Subject;
        entry.WhatWasLost.Should().Contain("Person 1");
        entry.Why.Should().Contain("rate limited");
    }

    [Fact]
    public async Task A_retried_then_successful_match_records_its_retry_count_on_a_completed_slice()
    {
        var attempts = 0;
        var match = new FakeMatchRunService((id, _) =>
            ++attempts < 3
                ? throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)
                : Task.FromResult(MatchOk(id)));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            match,
            NarrativeChat(Id(1), Id(1)));

        var outcome = await RunAsync(pipeline, UserId);

        var slice = outcome.Package.Slices.Should().ContainSingle(s => s.Stage == "match").Subject;
        slice.Status.Should().Be(StageSliceStatus.Completed);
        slice.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task A_cap_trip_before_the_fan_out_yields_skipped_slices_and_entries_matching_the_report_notes()
    {
        var usage = new ScriptedUsageService(new WindowUsage("daily", 50_000, 50_000, DateTimeOffset.UtcNow));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1), Candidate(2))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(2)),
            usage: usage);

        var outcome = await RunAsync(pipeline, UserId);

        var skipped = outcome.Package.Slices.Where(s => s.Status == StageSliceStatus.Skipped).ToList();
        skipped.Select(s => s.Stage).Should().Equal("match", "match", "narrative");
        skipped.Should().OnlyContain(s => s.InputTokens == 0 && s.OutputTokens == 0);
        skipped.Should().OnlyContain(s => s.DegradeReason!.Contains("cap"));

        var entry = outcome.Package.Degradations.Should().ContainSingle().Subject;
        entry.Why.Should().Be(
            "The daily token cap was reached after the shortlist step; match runs and the narrative were skipped.");
        outcome.Report!.Notes.Should().Contain(entry.Why, "the package mirrors the report's notes");
    }

    [Fact]
    public async Task A_shortlist_fault_still_hands_back_a_package_with_the_failed_slice()
    {
        var shortlist = new FakeShortlistRunService(new ShortlistRunOutcome(
            "shortlist", new AgentReply("[]", 100, 20, 120), Response: null,
            FaultDetail: "The semantic search backend is unavailable."));
        var pipeline = Pipeline(
            shortlist,
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(1)));

        var outcome = await RunAsync(pipeline, UserId);

        outcome.Report.Should().BeNull();
        var slice = outcome.Package.Slices.Should().ContainSingle().Subject;
        slice.Stage.Should().Be("shortlist");
        slice.Status.Should().Be(StageSliceStatus.Failed);
        slice.InputTokens.Should().Be(100, "the fault still spent tokens and the slice reports them");
        slice.DegradeReason.Should().Contain("semantic search backend");
        outcome.Package.Degradations.Should().ContainSingle().Which.Stage.Should().Be("shortlist");
    }

    [Fact]
    public async Task A_narrative_transport_failure_yields_a_failed_slice_and_a_degradation_entry()
    {
        var chat = new FakeChatClient(() => throw new HttpRequestException("model endpoint unreachable"));
        var pipeline = Pipeline(
            new FakeShortlistRunService(ShortlistOk(Candidate(1))),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            chat);

        var outcome = await RunAsync(pipeline, UserId);

        var slice = outcome.Package.Slices.Should().ContainSingle(s => s.Stage == "narrative").Subject;
        slice.Status.Should().Be(StageSliceStatus.Failed);
        slice.DegradeReason.Should().Contain("model endpoint unreachable");
        outcome.Package.Degradations.Should()
            .ContainSingle(d => d.Stage == "narrative")
            .Which.WhatWasLost.Should().Contain("rationales");
    }

    // ----- No credentials, by construction ------------------------------------------------------

    [Fact]
    public async Task The_serialized_package_carries_no_credential_shaped_keys()
    {
        var outcome = ShortlistOk(Candidate(1), Candidate(2));
        var pipeline = Pipeline(
            new FakeShortlistRunService(outcome with { ExtractionReply = new AgentReply("{}", 40, 10, 50) }),
            new FakeMatchRunService((id, _) => Task.FromResult(MatchOk(id))),
            NarrativeChat(Id(1), Id(2)));

        var run = await RunAsync(pipeline, UserId);
        var json = JsonSerializer.Serialize(run.Package, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var keys = new List<string>();
        CollectKeys(JsonDocument.Parse(json).RootElement, keys);

        string[] forbidden = ["secret", "authorization", "apikey", "credential", "password", "bearer"];
        keys.Should().NotContain(
            k => forbidden.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase)),
            "authorization state travels as provenance, never as credentials");
        // The only token-ish keys are the usage counters — nothing that could hold a bearer token.
        keys.Where(k => k.Contains("token", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(k => k == "inputTokens" || k == "outputTokens");
    }

    private static void CollectKeys(JsonElement element, List<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    keys.Add(property.Name);
                    CollectKeys(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectKeys(item, keys);
                }

                break;
        }
    }
}

/// <summary>The config-backed identity source: reads only ClientId and Scope from the agent's
/// McpAuth section — the secret key is never read, so it cannot leak into a package.</summary>
public class ConfigAgentIdentitySourceTests
{
    private static IConfiguration Config(Dictionary<string, string?> pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    [Fact]
    public void Finds_a_registered_agents_client_id_and_split_scopes()
    {
        var source = new ConfigAgentIdentitySource(Config(new()
        {
            ["McpAuth:shortlist:ClientId"] = "agent-shortlist",
            ["McpAuth:shortlist:ClientSecret"] = "never-read",
            ["McpAuth:shortlist:Scope"] = "mcp:read mcp:search",
        }));

        var identity = source.Find("shortlist");

        identity.Should().NotBeNull();
        identity!.ClientId.Should().Be("agent-shortlist");
        identity.Scopes.Should().Equal("mcp:read", "mcp:search");
    }

    [Fact]
    public void A_toolless_agent_without_a_config_section_resolves_to_null()
    {
        var source = new ConfigAgentIdentitySource(Config(new()
        {
            ["McpAuth:shortlist:ClientId"] = "agent-shortlist",
        }));

        source.Find("jd-extraction").Should().BeNull();
    }
}
