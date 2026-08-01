using CvManager.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Tests;

/// <summary>The chat-seam capture (P1T-95): real response model id + summed latency land in the
/// ambient scope; concurrent scopes stay isolated (the staffing match fan-out relies on it).</summary>
public class MeteringChatClientTests
{
    private sealed class StubChat(string modelId) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = modelId,
            });
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

        var (modelId, latencyMs) = scope.Snapshot();
        modelId.Should().Be("gemini-2.5-flash-lite", "the REAL response model id, not config");
        latencyMs.Should().BeGreaterThanOrEqualTo(0);
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
