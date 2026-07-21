using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Tests.Fakes;
using EmployeeManager.Agents.Usage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/staffing (one-shot JSON slice). They run against the real host
/// but swap the pipeline's step seams — the shortlist/match run services and the narrative chat
/// client — for fakes. The focus is request validation (400), the cap pre-check (429), the
/// shortlist-fault mapping (502), and the pinned camelCase report contract including the degraded
/// partial-result shape (200).
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

    private static FakeShortlistRunService ShortlistOk() => new(new ShortlistRunOutcome(
        "shortlist",
        new AgentReply("[]", 100, 20, 120),
        new ShortlistResponse(
            ["event streaming with Kafka", "Kubernetes operations", "team leadership"],
            [Candidate(AdaId, "Ada Lovelace"), Candidate(GraceId, "Grace Hopper")]),
        FaultDetail: null));

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
                extra?.Invoke(s);
            }));

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
    public async Task Returns_502_when_the_shortlist_step_faults()
    {
        var shortlist = new FakeShortlistRunService(
            _ => throw new HttpRequestException("model endpoint unreachable"));
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_502_when_the_shortlist_run_degrades_to_a_soft_fault()
    {
        var shortlist = new FakeShortlistRunService(new ShortlistRunOutcome(
            "shortlist", new AgentReply("[]", 100, 20, 120), Response: null,
            FaultDetail: "The semantic search backend is unavailable."));
        using var factory = FakedHost(shortlist);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_the_pinned_camel_case_report_contract()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/staffing",
            new { jobDescription = "Platform engineer: Kafka, Kubernetes, leadership.", matchTop = 2 });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

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
    public async Task Returns_200_with_a_degraded_report_when_one_match_fails()
    {
        var match = new FakeMatchRunService((id, _) =>
            id == GraceId
                ? throw new InvalidOperationException("cv_get exploded")
                : Task.FromResult(new MatchRunOutcome(
                    "match", "Overall score: 78/100\nOverall band: Strong", new AgentReply("a", 200, 50, 250))));
        using var factory = FakedHost(match: match);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "partial results ship as a degraded report, never an error");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("degraded").GetBoolean().Should().BeTrue();
        var failed = body.GetProperty("candidates")[1].GetProperty("match");
        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("error").GetString().Should().Contain("cv_get exploded");
        failed.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("notes").EnumerateArray().Should().Contain(
            n => n.GetString()!.Contains("Grace Hopper"));
    }

    [Fact]
    public async Task Records_usage_under_the_step_agent_names()
    {
        var meter = new RecordingUsageMeter();
        using var factory = FakedHost(extra: s => s.AddSingleton<IUsageMeter>(meter));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/staffing", new { jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        meter.Records.Select(r => r.AgentName).Should().Equal("shortlist", "match", "match", "staffing");
    }
}
