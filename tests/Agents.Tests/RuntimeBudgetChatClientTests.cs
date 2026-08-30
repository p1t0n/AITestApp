using CvManager.Agents.Agents;
using CvManager.Agents.Configuration;
using CvManager.Agents.Tests.Fakes;
using CvManager.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Tests;

/// <summary>
/// The Runtime Budget seam (P1T-147): a run that would blow past its ceiling still returns a real
/// answer, with tools withdrawn and the Degradation stated. Deterministic — the stub reports the
/// token counts, so no model and no wall-clock are involved.
/// </summary>
public class RuntimeBudgetChatClientTests
{
    /// <summary>A chat client that answers "ok" and reports a fixed input-token cost per call,
    /// recording exactly what options and messages each call was handed.</summary>
    private sealed class StubChat(long inputTokensPerCall) : IChatClient
    {
        public List<ChatOptions?> ReceivedOptions { get; } = [];
        public List<List<ChatMessage>> ReceivedMessages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedOptions.Add(options);
            ReceivedMessages.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = inputTokensPerCall },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The agents run the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static AIFunction Tool() => AIFunctionFactory.Create(() => "rows", "employee_list");

    private static ChatOptions WithTools() => new()
    {
        Tools = [Tool()],
        ToolMode = ChatToolMode.RequireAny,
    };

    /// <summary>The whole pipeline as production builds it: the budget outside, the metering seam
    /// (which feeds the budget its spend) inside, both within the function-invocation loop.</summary>
    private static (IChatClient Client, StubChat Inner) Pipeline(AgentBudget budget, long inputTokensPerCall)
    {
        var inner = new StubChat(inputTokensPerCall);
        return (new RuntimeBudgetChatClient(new MeteringChatClient(inner), "test-agent", budget), inner);
    }

    [Fact]
    public async Task Under_budget_the_call_passes_through_untouched()
    {
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 10_000, MaxIterations = 6 }, 1_000);
        var options = WithTools();

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        inner.ReceivedOptions.Should().AllSatisfy(o => o!.ToolMode.Should().Be(ChatToolMode.RequireAny));
        inner.ReceivedMessages.Should().AllSatisfy(m => m.Should().HaveCount(1));
        scope.Snapshot().Degradation.Should().BeNull();
    }

    [Fact]
    public async Task Over_the_token_ceiling_it_withdraws_the_tools_and_asks_for_a_closing_answer()
    {
        // 6,000 tokens a call: the second call starts with the 5,000-token ceiling already spent.
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 5_000, MaxIterations = 99 }, 6_000);
        var options = WithTools();

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        var closing = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        inner.ReceivedOptions[0]!.ToolMode.Should().Be(ChatToolMode.RequireAny, "the first call is under budget");
        inner.ReceivedOptions[1]!.ToolMode.Should().BeOfType<NoneChatToolMode>();
        inner.ReceivedMessages[1].Last().Text.Should().Be(RuntimeBudgetChatClient.ClosingInstruction);

        closing.Text.Should().StartWith("ok", "the model's real answer survives");
        closing.Text.Should().Contain("5,000").And.Contain("without further tool calls");
        scope.Snapshot().Degradation.Should().Contain("Runtime Budget reached").And.Contain("input tokens");
    }

    [Fact]
    public async Task The_iteration_backstop_catches_a_long_loop_of_individually_tiny_calls()
    {
        // Ten tokens a call never reaches a 100,000-token ceiling; three calls reach a 3-call one.
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 100_000, MaxIterations = 3 }, 10);
        var options = WithTools();

        using var scope = MeteringScope.Begin();
        for (var i = 0; i < 4; i++)
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, $"turn {i}")], options);
        }

        inner.ReceivedOptions.Take(3).Should().AllSatisfy(o => o!.ToolMode.Should().Be(ChatToolMode.RequireAny));
        inner.ReceivedOptions[3]!.ToolMode.Should().BeOfType<NoneChatToolMode>();
        scope.Snapshot().Degradation.Should().Contain("3 of 3 model calls");
    }

    [Fact]
    public async Task The_callers_options_are_cloned_not_mutated()
    {
        var (client, _) = Pipeline(new AgentBudget { MaxInputTokens = 1, MaxIterations = 99 }, 6_000);
        var options = WithTools();

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        options.ToolMode.Should().Be(ChatToolMode.RequireAny,
            "the loop reuses this instance — flipping it in place would disarm the agent's own wiring");
    }

    [Fact]
    public async Task A_schema_constrained_run_keeps_its_json_clean_and_records_the_degradation_instead()
    {
        var (client, _) = Pipeline(new AgentBudget { MaxInputTokens = 1, MaxIterations = 99 }, 6_000);
        var options = WithTools();
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(string)), "closing");

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        var closing = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        closing.Text.Should().Be("ok", "a prose note appended to a schema response breaks the parse");
        scope.Snapshot().Degradation.Should().Contain("Runtime Budget reached");
    }

    [Fact]
    public async Task An_already_closing_call_is_not_re_instructed()
    {
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 1, MaxIterations = 99 }, 6_000);
        var options = WithTools();

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);
        // A third call already carrying ToolMode.None has nothing left to withdraw.
        var alreadyClosed = new ChatOptions { Tools = [Tool()], ToolMode = ChatToolMode.None };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "three")], alreadyClosed);

        inner.ReceivedMessages[2].Should().ContainSingle()
            .Which.Text.Should().Be("three");
    }

    [Fact]
    public async Task A_tool_less_run_is_never_degraded()
    {
        // Code-driven agents (match, shortlist, roster-scan) send no tools: there is nothing to
        // withdraw, so the budget must stand down rather than inject a pointless closing turn.
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 1, MaxIterations = 1 }, 6_000);
        var options = new ChatOptions();

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        inner.ReceivedMessages.Should().AllSatisfy(m => m.Should().HaveCount(1));
        scope.Snapshot().Degradation.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_run_scope_the_budget_stands_down()
    {
        var (client, inner) = Pipeline(new AgentBudget { MaxInputTokens = 1, MaxIterations = 1 }, 6_000);
        var options = WithTools();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")], options);

        inner.ReceivedOptions.Should().AllSatisfy(o => o!.ToolMode.Should().Be(ChatToolMode.RequireAny));
    }

    [Fact]
    public async Task Concurrent_runs_spend_their_own_budgets()
    {
        // The wrapper is a singleton shared across requests; the spend it reads is per-run.
        var inner = new StubChat(6_000);
        var client = new RuntimeBudgetChatClient(
            new MeteringChatClient(inner), "test-agent",
            new AgentBudget { MaxInputTokens = 5_000, MaxIterations = 99 });

        async Task<string?> RunOneCallAsync()
        {
            using var scope = MeteringScope.Begin();
            await Task.Yield();
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], WithTools());
            return scope.Snapshot().Degradation;
        }

        var results = await Task.WhenAll(RunOneCallAsync(), RunOneCallAsync(), RunOneCallAsync());

        results.Should().AllSatisfy(d => d.Should().BeNull(
            "each run made a single call and none of them alone exceeds the ceiling"));
    }
}

/// <summary>
/// The seam driven through a real MAF <c>ChatClientAgent</c> loop rather than by hand: this is the
/// acceptance shape — a run that would keep calling tools forever comes back with a real answer,
/// a Degradation note and no dangling tool call.
/// </summary>
public class RuntimeBudgetAgentLoopTests
{
    /// <summary>Asks for the tool on every turn it is allowed to, and answers only once the tool
    /// mode says it cannot. Left unbounded it loops until MAF's own iteration ceiling.</summary>
    private sealed class GreedyToolCaller(long inputTokensPerCall) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var message = options?.ToolMode is NoneChatToolMode
                ? new ChatMessage(ChatRole.Assistant, "Ada Lovelace (id-1) knows React.")
                : new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent($"call-{Calls}", "employee_list", new Dictionary<string, object?>())]);

            return Task.FromResult(new ChatResponse(message)
            {
                Usage = new UsageDetails { InputTokenCount = inputTokensPerCall },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The agents run the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task A_runaway_roster_qa_run_still_returns_an_answer_carrying_the_degradation()
    {
        var model = new GreedyToolCaller(6_000);
        var budgeted = new RuntimeBudgetChatClient(
            new MeteringChatClient(model),
            "roster-qa",
            new AgentBudget { MaxInputTokens = 15_000, MaxIterations = 6 });
        var tool = AIFunctionFactory.Create(() => "Ada Lovelace;id-1;React", "employee_list");
        var agent = new RosterQaAgent(
            budgeted, new FakeToolSource(tool), NullLoggerFactory.Instance);

        var reply = await agent.AskAsync("Who knows React?");

        reply.Text.Should().Contain("Ada Lovelace", "the model writes the closing answer itself");
        reply.Text.Should().Contain("without further tool calls");
        reply.Degradation.Should().Contain("Runtime Budget reached");
        // 6,000 a call: three tool-calling turns spend 18,000, so the fourth opens over the
        // 15,000 ceiling and is the closing one. Four calls, not MAF's default forty.
        model.Calls.Should().Be(4);
    }
}

/// <summary>The wiring: no agent can resolve a model without its budget attached.</summary>
public class RuntimeBudgetWiringTests
{
    private static IServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Gemini:Model"] = "gemini-3.5-flash-lite",
            ["Gemini:ApiKey"] = "test-key",
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddGeminiChatClient(config);
        services.AddOptions<AgentBudgetOptions>().Bind(config.GetSection(AgentBudgetOptions.Section));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Every_agents_chat_client_comes_back_wrapped_in_its_runtime_budget()
    {
        var sp = BuildProvider();

        sp.ResolveAgentChatClient("roster-qa").Should().BeOfType<RuntimeBudgetChatClient>();
        sp.ResolveAgentChatClient("resume-ingestion").Should().BeOfType<RuntimeBudgetChatClient>();
        sp.ResolveAgentChatClient("an-agent-nobody-configured").Should().BeOfType<RuntimeBudgetChatClient>();
    }

    [Fact]
    public void The_wrapper_still_reports_the_underlying_models_metadata()
    {
        var sp = BuildProvider(("Gemini:Agents:cv-tailoring", "gemini-pro-latest"));

        var metadata = sp.ResolveAgentChatClient("cv-tailoring")
            .GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        metadata!.DefaultModelId.Should().Be("gemini-pro-latest");
    }
}

/// <summary>The budgets themselves: per-agent configuration with a default, not constants.</summary>
public class AgentBudgetOptionsTests
{
    [Fact]
    public void Ships_the_measured_budgets_for_the_two_model_driven_agents()
    {
        var options = new AgentBudgetOptions();

        options.For("roster-qa").MaxInputTokens.Should().Be(15_000);
        options.For("roster-qa").MaxIterations.Should().Be(6);
        // Bigger on both, for different reasons. Tokens: a pasted resume is real input — though
        // P1T-150 measured it at 3.5% of a reference run, so not the reason it was thought to be.
        // Iterations: the write surface has one tool per child, so an ordinary two-role resume is
        // 17 model calls before any self-correction. 8 degraded every run at call 8 of 17.
        options.For("resume-ingestion").MaxInputTokens.Should().Be(40_000);
        options.For("resume-ingestion").MaxIterations.Should().Be(24);
    }

    [Fact]
    public void An_unlisted_agent_falls_back_to_the_default()
    {
        var options = new AgentBudgetOptions();

        options.For("some-future-agent").MaxInputTokens.Should().Be(20_000);
        options.For("some-future-agent").MaxIterations.Should().Be(6);
    }

    [Fact]
    public void Configuration_overrides_one_agent_without_restating_the_rest()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentBudgets:Default:MaxInputTokens"] = "9000",
                ["AgentBudgets:Agents:roster-qa:MaxInputTokens"] = "1234",
            })
            .Build();

        var options = config.GetSection(AgentBudgetOptions.Section).Get<AgentBudgetOptions>()!;

        options.For("roster-qa").MaxInputTokens.Should().Be(1234);
        options.For("roster-qa").MaxIterations.Should().Be(6, "the untouched half of the row survives");
        options.For("resume-ingestion").MaxInputTokens.Should().Be(40_000, "other rows are untouched");
        options.For("anything-else").MaxInputTokens.Should().Be(9000);
    }
}
