using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the Roster Q&amp;A agent using a fake chat client + fake MCP tools.
/// No live model, no MCP server — these assert the wiring: tools reach the model, and the
/// agent runs the tool-call loop and returns the model's answer.
/// </summary>
public class RosterQaAgentTests
{
    private static AIFunction EmployeeListTool(Action onInvoke) =>
        AIFunctionFactory.Create(
            () => { onInvoke(); return "Ada Lovelace;id-1;React"; },
            "employee_list");

    [Fact]
    public async Task Wires_mcp_tools_into_the_model_and_returns_its_answer()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));
        var tool = EmployeeListTool(() => { });
        var agent = new RosterQaAgent(chat, new FakeToolSource(tool), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        answer.Text.Should().Contain("Ada Lovelace");
        chat.ReceivedOptions.Should().NotBeEmpty();
        chat.ReceivedOptions[0]!.Tools.Should().Contain(t => t.Name == "employee_list");
    }

    [Fact]
    public async Task Surfaces_token_usage_from_the_model_response()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React."))
            {
                Usage = new UsageDetails { InputTokenCount = 123, OutputTokenCount = 45, TotalTokenCount = 168 },
            });
        var agent = new RosterQaAgent(chat, new FakeToolSource(EmployeeListTool(() => { })), NullLoggerFactory.Instance);

        var reply = await agent.AskAsync("Who knows React?");

        reply.InputTokens.Should().Be(123);
        reply.OutputTokens.Should().Be(45);
        reply.TotalTokens.Should().Be(168);
    }

    [Fact]
    public async Task Invokes_a_tool_when_the_model_requests_it()
    {
        var toolInvoked = false;
        var tool = EmployeeListTool(() => toolInvoked = true);

        var chat = new FakeChatClient(
            // Turn 1: the model asks to call employee_list.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "employee_list", new Dictionary<string, object?>())])),
            // Turn 2: with the tool result in hand, the model answers.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));

        var agent = new RosterQaAgent(chat, new FakeToolSource(tool), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        toolInvoked.Should().BeTrue("the agent should run the tool the model asked for");
        answer.Text.Should().Contain("Ada Lovelace");
        chat.CallCount.Should().BeGreaterThanOrEqualTo(2, "one turn to request the tool, one to answer");
    }

    private static AIFunction SemanticSearchTool(Func<string> respond) =>
        AIFunctionFactory.Create((string query) => respond(), "roster_semantic_search");

    [Fact]
    public async Task Uses_semantic_search_for_a_capability_question_and_cites_its_snippet()
    {
        var semanticCalled = false;
        var semanticSearch = SemanticSearchTool(() =>
        {
            semanticCalled = true;
            return """{"results":[{"employeeId":"id-1","name":"Ada Lovelace","title":"Payments Lead","score":0.88,"snippets":["Led the fintech payments rewrite."]}]}""";
        });

        var chat = new FakeChatClient(
            // The model chooses semantic search for a "who has done X" question.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "roster_semantic_search",
                    new Dictionary<string, object?> { ["query"] = "fintech payments experience" }),
            ])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Ada Lovelace (id-1) led the fintech payments rewrite.")));

        var agent = new RosterQaAgent(
            chat,
            new FakeToolSource(EmployeeListTool(() => { }), semanticSearch),
            NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who has fintech payments experience?");

        semanticCalled.Should().BeTrue("capability questions should route to roster_semantic_search");
        answer.Text.Should().Contain("fintech payments rewrite");
        chat.ReceivedOptions[0]!.Tools.Should().Contain(t => t.Name == "roster_semantic_search");
    }

    [Fact]
    public async Task Falls_back_to_structured_tools_when_semantic_search_errors()
    {
        var listCalled = false;
        var semanticSearch = SemanticSearchTool(
            () => """{"results":[],"error":"The semantic search backend is unavailable."}""");
        var list = AIFunctionFactory.Create(() => { listCalled = true; return "Ada Lovelace;id-1;React"; }, "employee_list");

        var chat = new FakeChatClient(
            // Turn 1: semantic search returns a soft error.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "roster_semantic_search",
                    new Dictionary<string, object?> { ["query"] = "fintech" })])),
            // Turn 2: the model falls back to the structured list tool.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-2", "employee_list", new Dictionary<string, object?>())])),
            // Turn 3: it answers, noting the degraded path.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Semantic search was unavailable; from the roster, Ada Lovelace (id-1) is a candidate.")));

        var agent = new RosterQaAgent(
            chat,
            new FakeToolSource(semanticSearch, list),
            NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who has fintech experience?");

        listCalled.Should().BeTrue("the agent should fall back to structured tools on a semantic-search error");
        answer.Text.Should().Contain("Ada Lovelace");
    }
}
