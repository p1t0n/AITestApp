using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests.Fakes;

/// <summary>
/// A deterministic <see cref="IChatClient"/> that replays scripted responses. Lets the tests
/// drive the agent's tool-calling loop without a real model. Records the <see cref="ChatOptions"/>
/// it was handed so tests can assert the agent wired the MCP tools through.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _responses;

    public FakeChatClient(params Func<ChatResponse>[] responses) => _responses = new(responses);

    public List<ChatOptions?> ReceivedOptions { get; } = [];
    public List<IReadOnlyList<ChatMessage>> ReceivedMessages { get; } = [];
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        ReceivedOptions.Add(options);
        ReceivedMessages.Add(messages.ToList());
        // Replay each scripted response once; hold the last one for any extra turns.
        var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return Task.FromResult(next());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("These tests use the non-streaming path.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
