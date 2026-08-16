using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/interview-kit (P1T-102). Real host, fake chat model + tool
/// source — the focus is request validation, the pinned camelCase contract (answer + vetted
/// questions), evidence verification against the captured cv_get result, and the degrade chain.
/// </summary>
public class InterviewKitEndpointTests
{
    private const string CvPayload =
        """
        {"fullName":"Ada Lovelace","title":"Platform Lead","summary":"Payments platform engineer.","experiences":[{"id":"eeeeeee1-1111-1111-1111-111111111111","company":"Acme","title":"Senior Engineer","period":"Jan 2020 – Present","summary":"Platform work.","achievements":[{"id":"aaaaaaa1-1111-1111-1111-111111111111","text":"Cut deploy time 40%."}],"skills":["C#"]}]}
        """;

    private static AIFunction CvGetTool() =>
        AIFunctionFactory.Create((Guid employeeId) => CvPayload, "cv_get");

    /// <summary>Scripted happy path: the extractor's reply (call 1 since P1T-117), then cv_get
    /// call, markdown kit, questions JSON.</summary>
    private static FakeChatClient ScriptedChat(string? questionsJson = null) => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"requirements":[{"text":"platform engineering","kind":"Skill","priority":"Unspecified","minYears":null,"evidenceSpan":null,"inferred":true}],"seniority":"Unspecified","location":null,"ambiguities":[]}""")),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "cv_get",
                new Dictionary<string, object?> { ["employeeId"] = Guid.NewGuid() })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "## Interview kit\n\nQuestions below.")),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            questionsJson
            ?? """
               [{"question":"How was the 40% measured?","probes":"claim depth","evidence":"Cut deploy time 40%."},
                {"question":"Fabricated?","probes":"gap","evidence":"Shipped a Mars lander."}]
               """)));

    private static WebApplicationFactory<Program> FakedHost(IChatClient chat) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton(chat);
                s.AddKeyedSingleton<CvManager.Agents.Mcp.IMcpToolSource>(
                    "interview-kit", (_, _) => new FakeToolSource(CvGetTool()));
            }));

    [Fact]
    public async Task Returns_the_kit_with_vetted_questions_dropping_unverifiable_evidence()
    {
        using var factory = FakedHost(ScriptedChat());
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/interview-kit",
            new { employeeId = Guid.NewGuid(), jobDescription = "Platform engineer role." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().StartWith("## Interview kit");

        var questions = body.GetProperty("questions");
        questions.GetArrayLength().Should().Be(2);
        questions[0].GetProperty("question").GetString().Should().Be("How was the 40% measured?");
        questions[0].GetProperty("probes").GetString().Should().Be("claim depth");
        questions[0].GetProperty("evidence").GetString().Should().Be("Cut deploy time 40%.");
        // The fabricated quote is not in the captured CV: the question ships, its evidence doesn't.
        questions[1].GetProperty("evidence").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Corrupted_questions_turn_degrades_to_answer_only()
    {
        using var factory = FakedHost(ScriptedChat(questionsJson: "no json here"));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/interview-kit",
            new { employeeId = Guid.NewGuid(), jobDescription = "Role." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("questions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Validates_the_request_shape()
    {
        using var factory = FakedHost(ScriptedChat());
        using var client = factory.CreateAuthenticatedClient();

        (await client.PostAsJsonAsync("/agents/interview-kit",
                new { employeeId = Guid.Empty, jobDescription = "Role." }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/agents/interview-kit",
                new { employeeId = Guid.NewGuid(), jobDescription = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var factory = FakedHost(ScriptedChat());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/interview-kit",
            new { employeeId = Guid.NewGuid(), jobDescription = "Role." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
