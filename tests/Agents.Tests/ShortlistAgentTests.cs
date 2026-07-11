using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the Shortlist agent using a fake chat client + fake MCP tools.
/// No live model, no MCP server — these assert the wiring: the agent narrows the tool surface to
/// roster_shortlist_search, forwards the job description (and serialized filters) to the model,
/// and captures the tool call's arguments and result so the endpoint can compose the response
/// from tool-sourced data rather than model prose.
/// </summary>
public class ShortlistAgentTests
{
    private static readonly Guid AdaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string ToolPayload =
        """
        {"results":[{"employeeId":"11111111-1111-1111-1111-111111111111","name":"Ada Lovelace","title":"Platform Lead","score":0.91,"matchedCount":2,"totalRequirements":3,"evidence":[{"requirement":"event streaming with Kafka","matched":true,"snippet":"Built Kafka pipelines.","similarity":0.88},{"requirement":"Kubernetes operations","matched":true,"snippet":"Ran K8s clusters.","similarity":0.8},{"requirement":"team leadership","matched":false}]}],"error":null}
        """;

    private static AIFunction ShortlistTool(Action? onInvoke = null, string? payload = null) =>
        AIFunctionFactory.Create(
            (string[] requirements) => { onInvoke?.Invoke(); return payload ?? ToolPayload; },
            "roster_shortlist_search");

    private static AIFunction EmployeeListTool() =>
        AIFunctionFactory.Create(() => "Ada Lovelace;id-1", "employee_list");

    private static FakeChatClient ScriptedChat() => new(
        // Turn 1: the model extracts requirements from the JD and calls the shortlist tool.
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "roster_shortlist_search",
                new Dictionary<string, object?>
                {
                    ["requirements"] = new[] { "event streaming with Kafka", "Kubernetes operations", "team leadership" },
                })])),
        // Turn 2: with the tool result in hand, the model returns only the minimal rationale JSON.
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """[{"employeeId":"11111111-1111-1111-1111-111111111111","rationale":"Strong Kafka and K8s evidence."}]""")));

    [Fact]
    public async Task Exposes_only_the_shortlist_tool_to_the_model()
    {
        var chat = ScriptedChat();
        var agent = new ShortlistAgent(
            chat, new FakeToolSource(ShortlistTool(), EmployeeListTool()), NullLoggerFactory.Instance);

        await agent.ShortlistAsync(new ShortlistAgentRequest("Platform engineer role."));

        agent.Name.Should().Be("shortlist");
        chat.ReceivedOptions.Should().NotBeEmpty();
        chat.ReceivedOptions[0]!.Tools.Should().Contain(t => t.Name == "roster_shortlist_search");
        chat.ReceivedOptions[0]!.Tools.Should().NotContain(t => t.Name == "employee_list");
    }

    [Fact]
    public async Task Captures_the_tool_call_arguments_and_result_and_returns_the_models_json()
    {
        var toolInvoked = false;
        var agent = new ShortlistAgent(
            ScriptedChat(), new FakeToolSource(ShortlistTool(() => toolInvoked = true)), NullLoggerFactory.Instance);

        var outcome = await agent.ShortlistAsync(new ShortlistAgentRequest("Platform engineer role."));

        toolInvoked.Should().BeTrue("the agent should run the tool the model asked for");
        outcome.Requirements.Should().Equal(
            "event streaming with Kafka", "Kubernetes operations", "team leadership");
        outcome.Tool.Should().NotBeNull("the tool result must be captured for endpoint-side composition");
        outcome.Tool!.Error.Should().BeNull();
        outcome.Tool.Results.Should().HaveCount(1);
        var candidate = outcome.Tool.Results[0];
        candidate.EmployeeId.Should().Be(AdaId);
        candidate.Name.Should().Be("Ada Lovelace");
        candidate.Title.Should().Be("Platform Lead");
        candidate.Score.Should().BeApproximately(0.91, 0.0001);
        candidate.MatchedCount.Should().Be(2);
        candidate.TotalRequirements.Should().Be(3);
        candidate.Evidence.Should().HaveCount(3);
        candidate.Evidence[0].Snippet.Should().Be("Built Kafka pipelines.");
        candidate.Evidence[2].Matched.Should().BeFalse();
        outcome.Reply.Text.Should().Contain("Strong Kafka and K8s evidence.");
    }

    [Fact]
    public async Task Forwards_the_job_description_and_serialized_filters_to_the_model()
    {
        var chat = ScriptedChat();
        var agent = new ShortlistAgent(chat, new FakeToolSource(ShortlistTool()), NullLoggerFactory.Instance);

        var skillId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await agent.ShortlistAsync(new ShortlistAgentRequest(
            "Senior platform engineer, Kafka and Kubernetes.",
            AvailableOn: new DateOnly(2026, 8, 1),
            SkillIds: [skillId],
            Location: "Berlin",
            MinYears: 3m,
            TopK: 5));

        var allText = string.Join("\n", chat.ReceivedMessages.SelectMany(turn => turn).Select(m => m.Text));
        allText.Should().Contain("Senior platform engineer");
        allText.Should().Contain("2026-08-01");
        allText.Should().Contain("Berlin");
        allText.Should().Contain("22222222-2222-2222-2222-222222222222");
        allText.Should().Contain("topK");
        allText.Should().Contain("5");
        allText.Should().Contain("minYears");
    }

    [Fact]
    public async Task Tool_capture_is_null_when_the_model_never_calls_the_tool()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "I could not find anyone.")));
        var agent = new ShortlistAgent(chat, new FakeToolSource(ShortlistTool()), NullLoggerFactory.Instance);

        var outcome = await agent.ShortlistAsync(new ShortlistAgentRequest("Some role."));

        outcome.Tool.Should().BeNull();
        outcome.Requirements.Should().BeEmpty();
    }

    [Fact]
    public async Task Captures_a_soft_error_from_the_tool()
    {
        var agent = new ShortlistAgent(
            ScriptedChat(),
            new FakeToolSource(ShortlistTool(
                payload: """{"results":[],"error":"The semantic search backend is unavailable."}""")),
            NullLoggerFactory.Instance);

        var outcome = await agent.ShortlistAsync(new ShortlistAgentRequest("Some role."));

        outcome.Tool.Should().NotBeNull();
        outcome.Tool!.Error.Should().Be("The semantic search backend is unavailable.");
        outcome.Tool.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Surfaces_token_usage_from_the_model_response()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]"))
            {
                Usage = new UsageDetails { InputTokenCount = 123, OutputTokenCount = 45, TotalTokenCount = 168 },
            });
        var agent = new ShortlistAgent(chat, new FakeToolSource(ShortlistTool()), NullLoggerFactory.Instance);

        var outcome = await agent.ShortlistAsync(new ShortlistAgentRequest("Some role."));

        outcome.Reply.InputTokens.Should().Be(123);
        outcome.Reply.OutputTokens.Should().Be(45);
        outcome.Reply.TotalTokens.Should().Be(168);
    }
}
