using CvManager.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Tests;

/// <summary>The chat-seam capture (P1T-95): real response model id + summed latency land in the
/// ambient scope; concurrent scopes stay isolated (the staffing match fan-out relies on it).</summary>
public class MeteringChatClientTests
{
    private sealed class StubChat(string modelId, params string[][] toolCallsPerCall) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // The tools THIS call asks for, shaped as the model would return them.
            var requested = Calls < toolCallsPerCall.Length ? toolCallsPerCall[Calls] : [];
            Calls++;
            var message = new ChatMessage(ChatRole.Assistant, "ok");
            foreach (var tool in requested)
            {
                message.Contents.Add(new FunctionCallContent($"call-{tool}", tool, new Dictionary<string, object?>()));
            }

            return Task.FromResult(new ChatResponse(message) { ModelId = modelId });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task Reports_the_responses_model_id_and_accumulates_latency_across_calls()
    {
        var client = new MeteringChatClient(new StubChat("gemini-2.5-flash-lite"));

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")]);

        var run = scope.Snapshot();
        run.ModelId.Should().Be("gemini-2.5-flash-lite", "the REAL response model id, not config");
        run.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Counts_one_iteration_per_model_call()
    {
        var client = new MeteringChatClient(new StubChat("m"));

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "three")]);

        // This client sits INSIDE the function-invocation loop, so a call per iteration is the
        // Turn Amplification multiplier the ledger needs (P1T-144).
        scope.Snapshot().Iterations.Should().Be(3);
    }

    [Fact]
    public async Task Records_the_tool_sequence_in_call_order_including_repeats()
    {
        var client = new MeteringChatClient(new StubChat(
            "m",
            ["skill_list"],
            ["roster_semantic_search"],
            ["cv_get", "cv_get"],
            []));

        using var scope = MeteringScope.Begin();
        for (var i = 0; i < 4; i++)
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")]);
        }

        var run = scope.Snapshot();
        run.ToolSequence.Should().Be("skill_list,roster_semantic_search,cv_get,cv_get");
        run.Iterations.Should().Be(4, "the closing call that asks for no tool is still an iteration");
    }

    [Fact]
    public async Task A_run_that_calls_no_tool_has_a_null_sequence()
    {
        var client = new MeteringChatClient(new StubChat("m"));

        using var scope = MeteringScope.Begin();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")]);

        scope.Snapshot().ToolSequence.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_scope_the_call_passes_through_untouched()
    {
        var inner = new StubChat("m");
        var client = new MeteringChatClient(inner);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")]);

        response.Text.Should().Be("ok");
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_scopes_do_not_bleed_into_each_other()
    {
        async Task<string?> RunAsync(string model)
        {
            using var scope = MeteringScope.Begin();
            var client = new MeteringChatClient(new StubChat(model));
            await Task.Yield();
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")]);
            return scope.Snapshot().ModelId;
        }

        var results = await Task.WhenAll(RunAsync("model-a"), RunAsync("model-b"), RunAsync("model-c"));

        results.Should().Equal("model-a", "model-b", "model-c");
    }
}
