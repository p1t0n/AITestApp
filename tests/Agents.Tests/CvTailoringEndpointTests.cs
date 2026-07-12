using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeManager.Agents.Tests.Fakes;
using EmployeeManager.Agents.Usage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/cv-tailoring. They run against the real host but swap the chat
/// model and the tool source for fakes — the focus is request validation, cap enforcement,
/// metering, the pinned camelCase hybrid contract (answer + rewrites), the degrade chain, and the
/// upstream-fault philosophy (502 via Results.Problem).
/// </summary>
public class CvTailoringEndpointTests
{
    private const string Achievement1Text = "aaaaaaa1-1111-1111-1111-111111111111";
    private const string Experience1Text = "eeeeeee1-1111-1111-1111-111111111111";

    private const string CvPayload =
        """
        {"fullName":"Ada Lovelace","title":"Platform Lead","experiences":[{"id":"eeeeeee1-1111-1111-1111-111111111111","company":"Acme","title":"Senior Engineer","period":"Jan 2020 – Present","summary":"Platform work.","achievements":[{"id":"aaaaaaa1-1111-1111-1111-111111111111","text":"Cut deploy time 40%."}],"skills":["C#"]}]}
        """;

    private const string ExemplarPayload =
        """
        {"results":[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","exemplars":[{"text":"Reduced [company] settlement lag 55% by rebuilding the pipeline.","similarity":0.82}]}],"error":null}
        """;

    private static AIFunction CvGetTool() =>
        AIFunctionFactory.Create((Guid employeeId) => CvPayload, "cv_get");

    private static AIFunction ExemplarTool(string? payload = null) =>
        AIFunctionFactory.Create((Guid[] achievementIds) => payload ?? ExemplarPayload, "style_exemplar_search");

    /// <summary>The scripted happy path: cv_get, exemplar call, markdown answer, rewrites JSON.</summary>
    private static FakeChatClient ScriptedChat(string? rewritesJson = null) => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "cv_get",
                new Dictionary<string, object?> { ["employeeId"] = Guid.NewGuid() })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-2", "style_exemplar_search",
                new Dictionary<string, object?> { ["achievementIds"] = new[] { Achievement1Text } })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tailored summary.")),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            rewritesJson
            ?? $$"""[{"achievementId":"{{Achievement1Text}}","rewritten":"Cut deploy time 40% through release automation."}]""")));

    private static WebApplicationFactory<Program> FakedHost(
        IChatClient chat, Action<IServiceCollection>? extra = null, params AIFunction[] tools) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton(chat);
                s.AddKeyedSingleton<EmployeeManager.Agents.Mcp.IMcpToolSource>(
                    "cv-tailoring", (_, _) => new FakeToolSource(tools));
                extra?.Invoke(s);
            }));

    private static WebApplicationFactory<Program> DefaultHost(Action<IServiceCollection>? extra = null) =>
        FakedHost(ScriptedChat(), extra, CvGetTool(), ExemplarTool());

    [Fact]
    public async Task Returns_400_when_job_description_is_blank()
    {
        using var factory = DefaultHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_400_when_employee_id_is_empty()
    {
        using var factory = DefaultHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.Empty, jobDescription = "Senior React engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_429_when_a_usage_cap_is_exceeded()
    {
        var exceeded = new WindowUsage("daily", 25000, 25000, DateTimeOffset.UtcNow.AddHours(3));
        using var factory = DefaultHost(s => s.AddSingleton<IUsageService>(new FakeUsageService(exceeded)));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be("daily");
    }

    [Fact]
    public async Task Records_usage_under_the_cv_tailoring_agent_name()
    {
        var meter = new RecordingUsageMeter();
        using var factory = DefaultHost(s => s.AddSingleton<IUsageMeter>(meter));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        meter.Records.Should().ContainSingle().Which.AgentName.Should().Be("cv-tailoring");
    }

    [Fact]
    public async Task Returns_the_pinned_camel_case_hybrid_contract()
    {
        using var factory = DefaultHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer: Kubernetes, CI/CD." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("answer").GetString().Should().Be("Tailored summary.");

        var rewrites = body.GetProperty("rewrites");
        rewrites.GetArrayLength().Should().Be(1);
        var rewrite = rewrites[0];
        rewrite.GetProperty("experienceId").GetString().Should().Be(Experience1Text);
        rewrite.GetProperty("achievementId").GetString().Should().Be(Achievement1Text);
        rewrite.GetProperty("original").GetString().Should().Be("Cut deploy time 40%.");
        rewrite.GetProperty("rewritten").GetString().Should().Be("Cut deploy time 40% through release automation.");
    }

    [Fact]
    public async Task Drops_rewrites_with_ids_the_cv_does_not_contain()
    {
        using var factory = FakedHost(
            ScriptedChat("""[{"achievementId":"99999999-9999-9999-9999-999999999999","rewritten":"Invented deed."}]"""),
            null, CvGetTool(), ExemplarTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Be("Tailored summary.");
        body.GetProperty("rewrites").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Drops_a_rewrite_that_fabricates_a_number()
    {
        using var factory = FakedHost(
            ScriptedChat($$"""[{"achievementId":"{{Achievement1Text}}","rewritten":"Cut deploy time 75% overnight."}]"""),
            null, CvGetTool(), ExemplarTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rewrites").GetArrayLength().Should().Be(0, "75% is a fabricated number");
    }

    [Fact]
    public async Task Degrades_to_answer_only_when_turn_two_returns_prose()
    {
        using var factory = FakedHost(
            ScriptedChat("Here are the rewrites you asked for!"), null, CvGetTool(), ExemplarTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "unparseable turn-2 output must not fail the request");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Be("Tailored summary.");
        body.GetProperty("rewrites").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Still_rewrites_when_the_exemplar_tool_reports_a_soft_error()
    {
        using var factory = FakedHost(
            ScriptedChat(), null, CvGetTool(),
            ExemplarTool("""{"results":[],"error":"The embedding backend is unavailable."}"""));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a soft exemplar error must not fail the request");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rewrites").GetArrayLength().Should().Be(1, "rewrites are still attempted without exemplars");
    }

    [Fact]
    public async Task Keeps_answer_only_behavior_when_the_model_never_calls_the_tools()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "That employee was not found.")),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]")));
        using var factory = FakedHost(chat, null, CvGetTool(), ExemplarTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Contain("not found");
        body.GetProperty("rewrites").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Returns_502_when_the_model_endpoint_faults()
    {
        var chat = new FakeChatClient(
            () => throw new HttpRequestException("model endpoint unreachable"));
        using var factory = FakedHost(chat, null, CvGetTool(), ExemplarTool());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }
}
