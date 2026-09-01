using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Deterministic tests for the Roster Q&amp;A agent using a fake chat client + fake MCP tools.
/// No live model, no MCP server — these assert the wiring: tools reach the model, and the
/// agent runs the tool-call loop and returns the model's answer.
/// </summary>
public class RosterQaAgentTests
{
    private static AIFunction ExpertListTool(Action onInvoke) =>
        AIFunctionFactory.Create(
            () => { onInvoke(); return "Ada Lovelace;id-1;React"; },
            "expert_list");

    [Fact]
    public async Task Wires_mcp_tools_into_the_model_and_returns_its_answer()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));
        var tool = ExpertListTool(() => { });
        var agent = new RosterQaAgent(chat, new FakeToolSource(tool), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        answer.Text.Should().Contain("Ada Lovelace");
        chat.ReceivedOptions.Should().NotBeEmpty();
        chat.ReceivedOptions[0]!.Tools.Should().Contain(t => t.Name == "expert_list");
    }

    [Fact]
    public async Task Surfaces_token_usage_from_the_model_response()
    {
        // Grounded flow: tool call first, then the answer carrying the usage.
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React."))
            {
                Usage = new UsageDetails { InputTokenCount = 123, OutputTokenCount = 45, TotalTokenCount = 168 },
            });
        var agent = new RosterQaAgent(chat, new FakeToolSource(ExpertListTool(() => { })), NullLoggerFactory.Instance);

        var reply = await agent.AskAsync("Who knows React?");

        reply.InputTokens.Should().Be(123);
        reply.OutputTokens.Should().Be(45);
        reply.TotalTokens.Should().Be(168);
    }

    [Fact]
    public async Task Invokes_a_tool_when_the_model_requests_it()
    {
        var toolInvoked = false;
        var tool = ExpertListTool(() => toolInvoked = true);

        var chat = new FakeChatClient(
            // Turn 1: the model asks to call expert_list.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])),
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
            return """{"results":[{"expertId":"id-1","name":"Ada Lovelace","title":"Payments Lead","score":0.88,"snippets":["Led the fintech payments rewrite."]}]}""";
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
            new FakeToolSource(ExpertListTool(() => { }), semanticSearch),
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
        var list = AIFunctionFactory.Create(() => { listCalled = true; return "Ada Lovelace;id-1;React"; }, "expert_list");

        var chat = new FakeChatClient(
            // Turn 1: semantic search returns a soft error.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "roster_semantic_search",
                    new Dictionary<string, object?> { ["query"] = "fintech" })])),
            // Turn 2: the model falls back to the structured list tool.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-2", "expert_list", new Dictionary<string, object?>())])),
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

    // ----- First-call forcing + Capture-Verify Guard (P1T-130) -------------------------------

    [Fact]
    public async Task The_first_model_call_carries_RequireAny_and_later_iterations_reset_it()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));
        var agent = new RosterQaAgent(chat, new FakeToolSource(ExpertListTool(() => { })), NullLoggerFactory.Instance);

        await agent.AskAsync("Who knows React?");

        chat.ReceivedOptions[0]!.ToolMode.Should().Be(ChatToolMode.RequireAny,
            "the first call must be forced to ground itself in a tool");
        chat.ReceivedOptions[1]!.ToolMode.Should().NotBe(ChatToolMode.RequireAny,
            "the forcing is one-shot — the loop resets it so the model is free to answer");
    }

    [Fact]
    public async Task A_grounded_answer_passes_untouched_with_no_retry()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));
        var agent = new RosterQaAgent(chat, new FakeToolSource(ExpertListTool(() => { })), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        answer.Text.Should().Be("Ada Lovelace (id-1) knows React.");
        answer.Text.Should().NotContain("could not be grounded");
        chat.CallCount.Should().Be(2, "one turn to call the tool, one to answer — no guard retry");
    }

    [Fact]
    public async Task An_ungrounded_answer_gets_one_hardened_retry_that_can_recover()
    {
        var toolInvoked = false;
        var chat = new FakeChatClient(
            // Attempt 1: the model answers directly — nothing captured behind it.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Everyone knows React, probably.")),
            // Retry: it grounds itself and answers from the tool result.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")));
        var agent = new RosterQaAgent(
            chat, new FakeToolSource(ExpertListTool(() => toolInvoked = true)), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        toolInvoked.Should().BeTrue("the hardened retry grounded itself");
        answer.Text.Should().Be("Ada Lovelace (id-1) knows React.");
        answer.Text.Should().NotContain("could not be grounded", "the retry recovered — no degrade note");
        // The retry carried the hardened grounding instruction.
        chat.ReceivedMessages[1].Any(m => (m.Text ?? "").Contains("must ground your answer"))
            .Should().BeTrue("the retry adds the hardened instruction");
    }

    [Fact]
    public async Task A_second_ungrounded_answer_ships_with_the_degrade_note_and_summed_usage()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Probably Ada."))
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
            },
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Still guessing: Ada."))
            {
                Usage = new UsageDetails { InputTokenCount = 20, OutputTokenCount = 7, TotalTokenCount = 27 },
            });
        var agent = new RosterQaAgent(
            chat, new FakeToolSource(ExpertListTool(() => { })), NullLoggerFactory.Instance);

        var answer = await agent.AskAsync("Who knows React?");

        answer.Text.Should().StartWith("Still guessing: Ada.");
        answer.Text.Should().Contain("could not be grounded in roster data");
        // Both attempts spent tokens; both are reported for metering.
        answer.InputTokens.Should().Be(30);
        answer.OutputTokens.Should().Be(12);
        answer.TotalTokens.Should().Be(42);
    }
}
