using CvManager.Agents.Agents;
using CvManager.Agents.Usage;

namespace CvManager.Agents.Tests.Fakes;

/// <summary>An <see cref="IUsageService"/> stand-in with a fixed cap verdict, so endpoint tests
/// can exercise the 429 path without a database.</summary>
internal sealed class FakeUsageService(WindowUsage? exceeded = null) : IUsageService
{
    public Task<UsageSnapshot> GetSnapshotAsync(Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException("Endpoint tests only exercise FindExceededAsync.");

    public Task<WindowUsage?> FindExceededAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(exceeded);
}

/// <summary>An <see cref="IUsageMeter"/> that records calls in memory so endpoint tests can
/// assert the usage row's agent name without a database.</summary>
internal sealed class RecordingUsageMeter : IUsageMeter
{
    public List<(Guid UserId, string AgentName, AgentReply Reply)> Records { get; } = [];

    public Task RecordAsync(Guid userId, string agentName, AgentReply reply, CancellationToken ct = default)
    {
        Records.Add((userId, agentName, reply));
        return Task.CompletedTask;
    }
}
