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
    private string? _modelId;
    private long _latencyMs;

    private MeteringScope(MeteringScope? parent) => _parent = parent;

    public static MeteringScope Begin()
    {
        var scope = new MeteringScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    /// <summary>Called by <see cref="MeteringChatClient"/> after each chat call.</summary>
    public static void Report(string? modelId, long latencyMs)
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
        }
    }

    /// <summary>The run's totals so far: the last reported model id, summed model latency.</summary>
    public (string? ModelId, long LatencyMs) Snapshot()
    {
        lock (_lock)
        {
            return (_modelId, _latencyMs);
        }
    }

    public void Dispose() => Current.Value = _parent;
}

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
        MeteringScope.Report(response.ModelId, stopwatch.ElapsedMilliseconds);
        return response;
    }
}
