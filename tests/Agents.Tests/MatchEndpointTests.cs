using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/match. They run against the real host but swap the chat model
/// and the match tool source for fakes, so the host composes without a model key and no network is
/// touched — the focus is request validation and wiring, not the model.
/// </summary>
public class MatchEndpointTests
{
    private static readonly Guid AdaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GraceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static WebApplicationFactory<Program> FakedHost(Action<IServiceCollection>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IChatClient>(new FakeChatClient(
                    () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fit: MODERATE (60/100)"))));
                s.AddKeyedSingleton<CvManager.Agents.Mcp.IMcpToolSource>(
                    "match", (_, _) => new FakeToolSource());
                extra?.Invoke(s);
            }));

    private static ShortlistCandidateItem Candidate(Guid id, string name, double score) => new(
        id, name, "Engineer", score, new ShortlistCoverage(2, 2), [], "rationale");

    /// <summary>Swaps the JD-mode run seams for fakes: scripted shortlist + per-id match answers.</summary>
    private static WebApplicationFactory<Program> JdFakedHost() =>
        FakedHost(s =>
        {
            s.AddSingleton<IShortlistRunService>(new FakeShortlistRunService(new ShortlistRunOutcome(
                "shortlist",
                new AgentReply("[]", 100, 20, 120),
                new ShortlistResponse(["kafka"], [Candidate(AdaId, "Ada Lovelace", 0.95), Candidate(GraceId, "Grace Hopper", 0.80)]),
                FaultDetail: null)));
            s.AddSingleton<IMatchRunService>(new FakeMatchRunService((id, _) => Task.FromResult(
                new MatchRunOutcome(
                    "match",
                    "Analysis.",
                    new AgentReply("answer", 200, 50, 250),
                    Score: id == AdaId ? 82 : 55,
                    Band: id == AdaId ? "Strong" : "Moderate"))));
        });

    [Fact]
    public async Task Returns_400_when_job_description_is_blank()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.NewGuid(), jobDescription = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_400_when_employee_id_is_empty()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.Empty, jobDescription = "Senior React engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_the_agents_answer_for_a_valid_request()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.NewGuid(), jobDescription = "Senior React engineer, GraphQL." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Be("Fit: MODERATE (60/100)");
    }

    [Fact]
    public async Task Jd_only_request_returns_ranked_scored_candidates()
    {
        using var factory = JdFakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { jobDescription = "Kafka platform engineer.", topK = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requirements")[0].GetString().Should().Be("kafka");

        var results = body.GetProperty("results");
        results.GetArrayLength().Should().Be(2);
        var top = results[0];
        top.GetProperty("employeeId").GetGuid().Should().Be(AdaId);
        top.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        top.GetProperty("retrievalScore").GetDouble().Should().BeApproximately(0.95, 0.0001);
        top.GetProperty("status").GetString().Should().Be("completed");
        top.GetProperty("score").GetInt32().Should().Be(82);
        top.GetProperty("band").GetString().Should().Be("Strong");
        top.GetProperty("answer").GetString().Should().Contain("Analysis");
        top.TryGetProperty("error", out _).Should().BeFalse("errors are omitted when absent");
        results[1].GetProperty("score").GetInt32().Should().Be(55);
    }

    [Fact]
    public async Task Jd_only_request_maps_a_shortlist_fault_to_502()
    {
        using var factory = FakedHost(s => s.AddSingleton<IShortlistRunService>(
            new FakeShortlistRunService(new ShortlistRunOutcome(
                "shortlist", new AgentReply("x", 10, 5, 15), Response: null, FaultDetail: "retrieval down"))));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { jobDescription = "Any role." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Explicit_empty_employee_id_is_still_a_400_not_jd_mode()
    {
        using var factory = JdFakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.Empty, jobDescription = "Role." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
