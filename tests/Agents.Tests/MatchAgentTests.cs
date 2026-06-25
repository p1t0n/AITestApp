using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the Match agent using a fake chat client + fake MCP tools. No live
/// model, no MCP server — these assert the agent's wiring: it exposes only the CV tool to the
/// model, runs the tool-call loop, forwards the caller's request, and relays a not-found.
/// </summary>
public class MatchAgentTests
{
    private static AIFunction CvGetTool(Action onInvoke) =>
        AIFunctionFactory.Create(
            (Guid employeeId) => { onInvoke(); return """{"FullName":"Ada Lovelace","Title":"Engineer"}"""; },
            "cv_get");

    private static AIFunction EmployeeListTool() =>
        AIFunctionFactory.Create(() => "Ada Lovelace;id-1", "employee_list");

    [Fact]
    public async Task Exposes_only_the_cv_tool_to_the_model_and_returns_its_answer()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fit: MODERATE (68/100)")));
        var agent = new MatchAgent(
            chat, new FakeToolSource(CvGetTool(() => { }), EmployeeListTool()), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Assess Ada against a React role.");

        answer.Text.Should().Contain("MODERATE");
        agent.Name.Should().Be("match");
        chat.ReceivedOptions[0]!.Tools.Should().Contain(t => t.Name == "cv_get");
        chat.ReceivedOptions[0]!.Tools.Should().NotContain(t => t.Name == "employee_list");
    }

    [Fact]
    public async Task Invokes_cv_get_when_the_model_requests_it()
    {
        var toolInvoked = false;
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "cv_get",
                    new Dictionary<string, object?> { ["employeeId"] = Guid.Empty })])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fit: STRONG (90/100)")));

        var agent = new MatchAgent(
            chat, new FakeToolSource(CvGetTool(() => toolInvoked = true)), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Assess Ada against a React role.");

        toolInvoked.Should().BeTrue("the agent should run the cv_get tool the model asked for");
        answer.Text.Should().Contain("STRONG");
        chat.CallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Forwards_the_callers_request_including_the_job_description_to_the_model()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fit: MODERATE (60/100)")));
        var agent = new MatchAgent(chat, new FakeToolSource(CvGetTool(() => { })), NullLoggerFactory.Instance);

        const string request = "Employee 11111111-2222-3333-4444-555555555555. Job description: Senior React engineer, GraphQL.";
        await agent.AskAsync(request);

        chat.ReceivedMessages.SelectMany(turn => turn).Select(m => m.Text)
            .Should().Contain(t => t.Contains("Senior React engineer") && t.Contains("11111111-2222"));
    }

    [Fact]
    public async Task Relays_a_not_found_from_cv_get_as_plain_prose()
    {
        var notFoundTool = AIFunctionFactory.Create(
            (Guid employeeId) => """{"error":"not_found","message":"Employee not found."}""", "cv_get");
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "cv_get",
                    new Dictionary<string, object?> { ["employeeId"] = Guid.Empty })])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "That employee was not found.")));

        var agent = new MatchAgent(chat, new FakeToolSource(notFoundTool), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Assess employee 00000000-0000-0000-0000-000000000000 for a role.");

        answer.Text.Should().Contain("not found");
    }
}
