using ExpertToJob.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The Tool Result Budget seam (EXP-40): one tool result is never shown to the model larger than
/// the agent's ceiling. The Runtime Budget checks spend BEFORE a call, so a single 503-row
/// <c>expert_list</c> used to ride into the next call whole — 54,669 tokens for one roster-qa
/// question against a 50,000-token day.
/// </summary>
public class ToolResultBudgetChatClientTests
{
    private sealed class RecordingChat : IChatClient
    {
        public List<List<ChatMessage>> ReceivedMessages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The agents run the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static (ToolResultBudgetChatClient Client, RecordingChat Inner) Pipeline(long maxToolResultTokens)
    {
        var inner = new RecordingChat();
        return (new ToolResultBudgetChatClient(
            inner, "test-agent", new AgentBudget { MaxToolResultTokens = maxToolResultTokens }), inner);
    }

    /// <summary>A user turn, the model's call for <paramref name="tool"/>, and its result.</summary>
    private static List<ChatMessage> Conversation(string tool, object result) =>
    [
        new(ChatRole.User, "who is available from backend devs"),
        new(ChatRole.Assistant, [new FunctionCallContent("call-1", tool, new Dictionary<string, object?>())]),
        new(ChatRole.Tool, [new FunctionResultContent("call-1", result)]),
    ];

    private static FunctionResultContent ResultSent(RecordingChat inner) =>
        inner.ReceivedMessages.Single().SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();

    [Fact]
    public async Task An_oversized_tool_result_is_withheld_from_the_model()
    {
        // 40,000 characters is 10,000 estimated tokens — twice a 5,000 ceiling.
        var (client, inner) = Pipeline(5_000);

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync(Conversation("expert_list", new string('x', 40_000)));

        var sent = ResultSent(inner);
        sent.CallId.Should().Be("call-1", "the model's call still gets its answer, just not the payload");
        var text = sent.Result.Should().BeOfType<string>().Subject;
        text.Should().NotContain("xxxx");
        text.Should().Contain("expert_list").And.Contain("10,000").And.Contain("5,000")
            .And.Contain("filter");
    }

    [Fact]
    public async Task A_result_at_the_ceiling_passes_through_as_the_same_object()
    {
        // Exactly 5,000 estimated tokens: at the limit is inside it.
        var (client, inner) = Pipeline(5_000);
        var payload = new string('x', 20_000);
        var conversation = Conversation("expert_list", payload);

        await client.GetResponseAsync(conversation);

        inner.ReceivedMessages.Single().Should().Equal(conversation,
            "an agent that never produces an oversized result must see no change at all");
    }

    [Fact]
    public async Task The_callers_conversation_is_not_mutated()
    {
        var (client, _) = Pipeline(5_000);
        var payload = new string('x', 40_000);
        var conversation = Conversation("expert_list", payload);
        var toolMessage = conversation[2];

        await client.GetResponseAsync(conversation);

        conversation[2].Should().BeSameAs(toolMessage);
        toolMessage.Contents.OfType<FunctionResultContent>().Single().Result.Should().BeSameAs(payload,
            "the loop keeps this list — the withholding repeats on every later iteration instead");
    }

    [Fact]
    public async Task Withholding_a_result_records_the_degradation_on_the_run()
    {
        var (client, _) = Pipeline(5_000);

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync(Conversation("expert_list", new string('x', 40_000)));

        scope.Snapshot().Degradation.Should().Contain("Tool Result Budget reached")
            .And.Contain("expert_list");
    }

    [Fact]
    public async Task A_structured_mcp_result_is_measured_by_its_serialized_size()
    {
        // MCP results arrive as JSON, not strings: 600 roster-shaped rows is the 503-expert case.
        var rows = Enumerable.Range(0, 600).Select(i => new
        {
            id = Guid.NewGuid(), firstName = "Ada", lastName = $"Lovelace{i}",
            email = $"ada{i}@example.com", title = "Backend Developer", currentCapacityPercent = 50,
        });
        var json = System.Text.Json.JsonSerializer.SerializeToElement(rows);
        var (client, inner) = Pipeline(5_000);

        await client.GetResponseAsync(Conversation("expert_list", json));

        ResultSent(inner).Result.Should().BeOfType<string>()
            .Which.Should().Contain("Tool Result Budget reached");
    }

    [Fact]
    public void The_notice_reads_the_same_number_whatever_culture_the_host_has()
    {
        var text = Culture.Under(Culture.Other, async () =>
        {
            var (client, inner) = Pipeline(5_000);
            await client.GetResponseAsync(Conversation("expert_list", new string('x', 40_000)));
            return (string)ResultSent(inner).Result!;
        });

        text.Should().Contain("10,000").And.Contain("5,000");
        text.Should().NotContain("5.000");
    }
}

/// <summary>
/// The acceptance shape (EXP-40), through the real wiring: roster-qa resolved by
/// <c>ResolveAgentChatClient</c>, driven by a real MAF loop, asks for <c>expert_list</c> on a
/// 503-expert-sized roster — and the model never sees the roster, only the withholding notice.
/// </summary>
public class ToolResultBudgetAgentLoopTests
{
    /// <summary>Calls expert_list once, then answers; records every tool result it was shown.</summary>
    private sealed class ListingModel : IChatClient
    {
        public List<object?> ToolResultsSeen { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
            ToolResultsSeen.AddRange(results.Select(r => r.Result));
            var message = results.Count == 0
                ? new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "expert_list", new Dictionary<string, object?>())])
                : new ChatMessage(ChatRole.Assistant, "The roster is too large to list; try a filtered search.");
            return Task.FromResult(new ChatResponse(message));
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
    public async Task A_whole_roster_expert_list_never_reaches_roster_qas_model()
    {
        var model = new ListingModel();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddKeyedSingleton<IChatClient>(services, "roster-qa", model);
        var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);

        // ~45,000 characters: the shape of expert_list at 503 experts (2,805 estimated tokens at 45).
        var roster = string.Join('\n', Enumerable.Range(0, 503).Select(i =>
            $"{Guid.NewGuid()};Expert{i};Backend Developer;expert{i}@example.com;50"));
        var tool = AIFunctionFactory.Create(() => roster, "expert_list");
        var agent = new ExpertToJob.Agents.Agents.RosterQaAgent(
            Configuration.ChatProviderServiceCollectionExtensions.ResolveAgentChatClient(sp, "roster-qa"),
            new Fakes.FakeToolSource(tool),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var reply = await agent.AskAsync("who is available from backend devs");

        model.ToolResultsSeen.Should().NotBeEmpty();
        model.ToolResultsSeen.Should().AllSatisfy(r =>
            r!.ToString()!.Should().StartWith("Tool Result Budget reached").And.Match(s => s.Length < 1_000));
        reply.Degradation.Should().Contain("Tool Result Budget reached");
    }
}
