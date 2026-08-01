using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CvManager.Agents.Tests.Fakes;
using CvManager.Agents.Usage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/shortlist. They run against the real host but swap the chat
/// model, the shortlist tool source, and (where relevant) the usage services for fakes — the focus
/// is request validation, cap enforcement, metering, the pinned camelCase response contract, and
/// the upstream-fault philosophy (502 via Results.Problem).
/// </summary>
public class ShortlistEndpointTests
{
    private const string AdaIdText = "11111111-1111-1111-1111-111111111111";

    private const string ToolPayload =
        """
        {"results":[{"employeeId":"11111111-1111-1111-1111-111111111111","name":"Ada Lovelace","title":"Platform Lead","score":0.91,"matchedCount":2,"totalRequirements":3,"evidence":[{"requirement":"event streaming with Kafka","matched":true,"snippet":"Built Kafka pipelines.","similarity":0.88},{"requirement":"Kubernetes operations","matched":true,"snippet":"Ran K8s clusters.","similarity":0.8},{"requirement":"team leadership","matched":false}]}],"error":null}
        """;

    private static AIFunction ShortlistTool(string? payload = null) =>
        AIFunctionFactory.Create((string[] requirements) => payload ?? ToolPayload, "roster_shortlist_search");

    private static FakeChatClient ScriptedChat() => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "roster_shortlist_search",
                new Dictionary<string, object?>
                {
                    ["requirements"] = new[] { "event streaming with Kafka", "Kubernetes operations", "team leadership" },
                })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""[{"employeeId":"{{AdaIdText}}","rationale":"Strong Kafka and K8s evidence."}]""")));

    private static WebApplicationFactory<Program> FakedHost(
        IChatClient chat, AIFunction? tool = null, Action<IServiceCollection>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton(chat);
                s.AddKeyedSingleton<CvManager.Agents.Mcp.IMcpToolSource>(
                    "shortlist", (_, _) => tool is null ? new FakeToolSource() : new FakeToolSource(tool));
                extra?.Invoke(s);
            }));

    [Fact]
    public async Task Returns_400_when_job_description_is_blank()
    {
        using var factory = FakedHost(ScriptedChat(), ShortlistTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/shortlist", new { jobDescription = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_429_when_a_usage_cap_is_exceeded()
    {
        var exceeded = new WindowUsage("daily", 25000, 25000, DateTimeOffset.UtcNow.AddHours(3));
        using var factory = FakedHost(ScriptedChat(), ShortlistTool(),
            s => s.AddSingleton<IUsageService>(new FakeUsageService(exceeded)));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be("daily");
    }

    [Fact]
    public async Task Records_usage_under_the_shortlist_agent_name()
    {
        var meter = new RecordingUsageMeter();
        using var factory = FakedHost(ScriptedChat(), ShortlistTool(),
            s => s.AddSingleton<IUsageMeter>(meter));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        meter.Records.Should().ContainSingle().Which.AgentName.Should().Be("shortlist");
    }

    [Fact]
    public async Task Returns_the_pinned_camel_case_contract_composed_from_the_tool_result()
    {
        using var factory = FakedHost(ScriptedChat(), ShortlistTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist",
            new { jobDescription = "Platform engineer: Kafka, Kubernetes, leadership.", topK = 5 });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var requirements = body.GetProperty("requirements");
        requirements.GetArrayLength().Should().Be(3);
        requirements[0].GetString().Should().Be("event streaming with Kafka");

        var candidates = body.GetProperty("candidates");
        candidates.GetArrayLength().Should().Be(1);
        var ada = candidates[0];
        ada.GetProperty("employeeId").GetString().Should().Be(AdaIdText);
        ada.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        ada.GetProperty("title").GetString().Should().Be("Platform Lead");
        ada.GetProperty("score").GetDouble().Should().BeApproximately(0.91, 0.0001);
        ada.GetProperty("coverage").GetProperty("matched").GetInt32().Should().Be(2);
        ada.GetProperty("coverage").GetProperty("total").GetInt32().Should().Be(3);
        ada.GetProperty("rationale").GetString().Should().Be("Strong Kafka and K8s evidence.");

        var perRequirement = ada.GetProperty("requirements");
        perRequirement.GetArrayLength().Should().Be(3);
        perRequirement[0].GetProperty("text").GetString().Should().Be("event streaming with Kafka");
        perRequirement[0].GetProperty("matched").GetBoolean().Should().BeTrue();
        perRequirement[0].GetProperty("snippet").GetString().Should().Be("Built Kafka pipelines.");
        perRequirement[2].GetProperty("matched").GetBoolean().Should().BeFalse();
        perRequirement[2].TryGetProperty("snippet", out _).Should().BeFalse("null snippets are omitted");
    }

    [Fact]
    public async Task Degrades_to_templated_rationales_when_the_model_returns_prose()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "roster_shortlist_search",
                    new Dictionary<string, object?> { ["requirements"] = new[] { "Kafka" } })])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Ada seems like a great fit for this role!")));
        using var factory = FakedHost(chat, ShortlistTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "unparseable model prose must not fail the request");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("candidates")[0].GetProperty("rationale").GetString()
            .Should().Be("Matched 2/3 requirements: event streaming with Kafka, Kubernetes operations; missing: team leadership.");
    }

    [Fact]
    public async Task Returns_502_when_the_model_endpoint_faults()
    {
        var chat = new FakeChatClient(
            () => throw new HttpRequestException("model endpoint unreachable"));
        using var factory = FakedHost(chat, ShortlistTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_502_when_the_tool_reports_a_soft_retrieval_error()
    {
        using var factory = FakedHost(
            ScriptedChat(),
            ShortlistTool("""{"results":[],"error":"The semantic search backend is unavailable."}"""));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_502_when_the_model_never_calls_the_shortlist_tool()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "No tool needed, trust me.")));
        using var factory = FakedHost(chat, ShortlistTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }
}
