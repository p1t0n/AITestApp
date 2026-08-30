using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Usage;

/// <summary>
/// Per-run capture cell for chat-call telemetry (P1T-95). An agent opens a scope around its run;
/// every chat call the run makes (tool round-trips included) reports its real response model id
/// and wall-clock latency here through <see cref="MeteringChatClient"/>. AsyncLocal makes the
/// scope flow into the run's async graph while staying isolated from concurrent runs (the
/// staffing match fan-out runs several scopes side by side).
/// <para>
/// It is also the run boundary the Runtime Budget spends against (P1T-147): the scope every agent
/// already opens around a run is exactly the unit a per-run ceiling has to be measured over, so
/// <see cref="RuntimeBudgetChatClient"/> reads <see cref="Spend"/> here rather than establishing a
/// second ambient scope that every agent would then have to remember to open.
/// </para>
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
    private long _inputTokens;
    private string? _degradation;

    private MeteringScope(MeteringScope? parent) => _parent = parent;

    public static MeteringScope Begin()
    {
        var scope = new MeteringScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    /// <summary>Called by <see cref="MeteringChatClient"/> after each chat call. One call is one
    /// Iteration; <paramref name="toolCalls"/> are the tools that call asked for, in order;
    /// <paramref name="inputTokens"/> is what that call sent, summed into the run's spend.</summary>
    public static void Report(
        string? modelId, long latencyMs, IReadOnlyList<string>? toolCalls = null, long inputTokens = 0)
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
            scope._inputTokens += inputTokens;
            if (toolCalls is { Count: > 0 })
            {
                scope._toolCalls.AddRange(toolCalls);
            }
        }
    }

    /// <summary>What the ambient run has spent so far, or null when there is no run scope —
    /// the Runtime Budget cannot bound what it cannot measure, so it stands down (P1T-147).</summary>
    public static RunSpend? Spend()
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return null;
        }

        lock (scope._lock)
        {
            return new RunSpend(scope._inputTokens, scope._iterations);
        }
    }

    /// <summary>Records an explicit Degradation on the ambient run — a step that was withdrawn
    /// rather than failed. First one wins: the ceiling that stopped the run is the honest reason,
    /// and later calls under the same ceiling are consequences, not new causes.</summary>
    public static void ReportDegradation(string reason)
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return;
        }

        lock (scope._lock)
        {
            scope._degradation ??= reason;
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
                _toolCalls.Count == 0 ? null : string.Join(',', _toolCalls),
                _degradation);
        }
    }

    public void Dispose() => Current.Value = _parent;
}

/// <summary>What a run has spent against its Runtime Budget so far (P1T-147).</summary>
/// <param name="InputTokens">Input tokens summed across the run's model calls.</param>
/// <param name="Iterations">Model calls the run has already made.</param>
public readonly record struct RunSpend(long InputTokens, int Iterations);

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
/// <param name="Degradation">Why the run was cut short of what it set out to do, or null when it
/// ran whole. Today the only writer is the Runtime Budget (P1T-147).</param>
public sealed record MeteringSnapshot(
    string? ModelId, long LatencyMs, int Iterations, string? ToolSequence, string? Degradation = null);

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
        MeteringScope.Report(
            response.ModelId,
            stopwatch.ElapsedMilliseconds,
            toolCalls,
            response.Usage?.InputTokenCount ?? 0);
        return response;
    }
}
