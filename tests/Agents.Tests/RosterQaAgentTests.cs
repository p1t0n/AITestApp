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
}
