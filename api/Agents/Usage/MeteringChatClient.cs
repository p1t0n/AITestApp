using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Usage;

/// <summary>
/// Per-run capture cell for chat-call telemetry (P1T-95). An agent opens a scope around its run;
/// every chat call the run makes (tool round-trips included) reports its real response model id
/// and wall-clock latency here through <see cref="MeteringChatClient"/>. AsyncLocal makes the
/// scope flow into the run's async graph while staying isolated from concurrent runs (the
/// staffing match fan-out runs several scopes side by side).
/// </summary>
public sealed class MeteringScope : IDisposable
{
    private static readonly AsyncLocal<MeteringScope?> Current = new();

    private readonly MeteringScope? _parent;
    private readonly Lock _lock = new();
    private readonly List<string> _toolCalls = [];
    private string? _modelId;
    private long _latencyMs;
    private int _iterations;

    private MeteringScope(MeteringScope? parent) => _parent = parent;

    public static MeteringScope Begin()
    {
        var scope = new MeteringScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    /// <summary>Called by <see cref="MeteringChatClient"/> after each chat call. One call is one
    /// Iteration; <paramref name="toolCalls"/> are the tools that call asked for, in order.</summary>
    public static void Report(string? modelId, long latencyMs, IReadOnlyList<string>? toolCalls = null)
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return;
        }

        lock (scope._lock)
        {
            scope._modelId = modelId ?? scope._modelId;
            scope._latencyMs += latencyMs;
            scope._iterations++;
            if (toolCalls is { Count: > 0 })
            {
                scope._toolCalls.AddRange(toolCalls);
            }
        }
    }

    /// <summary>The run's totals so far.</summary>
    public MeteringSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new MeteringSnapshot(
                _modelId,
                _latencyMs,
                _iterations,
                _toolCalls.Count == 0 ? null : string.Join(',', _toolCalls));
        }
    }

    public void Dispose() => Current.Value = _parent;
}

/// <summary>
/// What a run cost and why (P1T-144). <see cref="Iterations"/> is the number of model calls the
/// run made — the Turn Amplification multiplier — and <see cref="ToolSequence"/> the ordered,
/// comma-separated tool names it asked for. Together they turn "this call cost 146,647 tokens"
/// into a diagnosable row without anyone attaching a throwaway <c>ActivityListener</c>.
/// </summary>
/// <param name="ModelId">The last model id the run's responses reported.</param>
/// <param name="LatencyMs">Summed model wall-clock time across the run's calls.</param>
/// <param name="Iterations">Model calls made in the run.</param>
/// <param name="ToolSequence">Ordered tool names, comma-separated; null when no tool was called.</param>
public sealed record MeteringSnapshot(string? ModelId, long LatencyMs, int Iterations, string? ToolSequence);

/// <summary>
/// The chat-client seam of the usage ledger: measures every model call and reports the response's
/// REAL model id (the config-based label in <see cref="UsageMeter"/> went stale the moment the
/// provider migrated) plus latency into the ambient <see cref="MeteringScope"/>. No scope → no-op.
/// </summary>
public sealed class MeteringChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        // The tools this call ASKED for, read off the response before FunctionInvokingChatClient
        // runs them — this client sits inside the invocation loop, so it sees every iteration.
        var toolCalls = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => c.Name)
            .ToList();
        MeteringScope.Report(response.ModelId, stopwatch.ElapsedMilliseconds, toolCalls);
        return response;
    }
}
