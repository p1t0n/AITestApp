using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Usage;

namespace EmployeeManager.Agents.Tests.Fakes;

/// <summary>An <see cref="IShortlistRunService"/> stand-in: replays a scripted outcome (or throws)
/// and records the requests it received, so pipeline tests can pin the clamped topK. The
/// token-aware overload lets streaming tests observe the pipeline's cancellation token.</summary>
internal sealed class FakeShortlistRunService(
    Func<ShortlistAgentRequest, CancellationToken, Task<ShortlistRunOutcome>> run)
    : IShortlistRunService
{
    public FakeShortlistRunService(ShortlistRunOutcome outcome)
        : this((_, _) => Task.FromResult(outcome))
    {
    }

    public FakeShortlistRunService(Func<ShortlistAgentRequest, Task<ShortlistRunOutcome>> run)
        : this((request, _) => run(request))
    {
    }

    public List<ShortlistAgentRequest> Requests { get; } = [];

    public Task<ShortlistRunOutcome> RunAsync(ShortlistAgentRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return run(request, ct);
    }
}

/// <summary>An <see cref="IMatchRunService"/> stand-in driven by a per-call delegate; counts calls
/// (thread-safely — the pipeline fans match runs out in parallel) and records what it was asked.</summary>
internal sealed class FakeMatchRunService(Func<Guid, string, Task<MatchRunOutcome>> run) : IMatchRunService
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public List<(Guid EmployeeId, string JobDescription)> Requests { get; } = [];

    public Task<MatchRunOutcome> RunAsync(Guid employeeId, string jobDescription, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
        {
            Requests.Add((employeeId, jobDescription));
        }

        return run(employeeId, jobDescription);
    }
}

/// <summary>An <see cref="IUsageService"/> whose <c>FindExceededAsync</c> replays scripted verdicts
/// in order (null once the script runs out) — lets tests trip the cap at a chosen pipeline stage.</summary>
internal sealed class ScriptedUsageService(params WindowUsage?[] verdicts) : IUsageService
{
    private readonly Queue<WindowUsage?> _verdicts = new(verdicts);

    public int CapChecks { get; private set; }

    public Task<UsageSnapshot> GetSnapshotAsync(Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException("Pipeline tests only exercise FindExceededAsync.");

    public Task<WindowUsage?> FindExceededAsync(Guid userId, CancellationToken ct = default)
    {
        CapChecks++;
        return Task.FromResult(_verdicts.Count > 0 ? _verdicts.Dequeue() : null);
    }
}
