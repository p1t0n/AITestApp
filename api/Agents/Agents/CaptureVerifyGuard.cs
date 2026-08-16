using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

/// <summary>
/// The Capture-Verify Guard's capture half (P1T-130, see CONTEXT.md: Capture-Verify Guard): an
/// ambient per-run scope recording whether any tool invocation actually returned a result. The
/// same AsyncLocal pattern as <see cref="Usage.MeteringScope"/> — the agent instance (and its
/// wrapped tools) is cached and shared across concurrent runs, so per-run state cannot live on
/// the tools; it rides the async context of the run that invoked them.
/// </summary>
public sealed class CaptureScope : IDisposable
{
    private static readonly AsyncLocal<CaptureScope?> Current = new();

    private readonly CaptureScope? _parent;
    private int _captures;

    private CaptureScope(CaptureScope? parent) => _parent = parent;

    public static CaptureScope Begin()
    {
        var scope = new CaptureScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    /// <summary>Called by the capturing tool wrapper after a successful invocation.</summary>
    public static void Report()
    {
        if (Current.Value is { } scope)
        {
            Interlocked.Increment(ref scope._captures);
        }
    }

    /// <summary>True once at least one tool returned a result within this scope — the answer has
    /// something real behind it.</summary>
    public bool Captured => Volatile.Read(ref _captures) > 0;

    public void Dispose() => Current.Value = _parent;
}

/// <summary>Wraps an agent's tools so every successful invocation reports into the ambient
/// <see cref="CaptureScope"/>. Name, description, and schema pass through unchanged — the model
/// sees exactly the tools it would have seen.</summary>
public static class CaptureVerifyGuard
{
    public static IList<AITool> WrapTools(IEnumerable<AITool> tools) =>
        tools.Select(t => t is AIFunction f ? (AITool)new CapturingFunction(f) : t).ToList();

    private sealed class CapturingFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            if (result is not null)
            {
                CaptureScope.Report();
            }

            return result;
        }
    }
}
