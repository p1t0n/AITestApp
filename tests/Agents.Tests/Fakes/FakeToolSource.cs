using ExpertToJob.Agents.Mcp;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests.Fakes;

/// <summary>An <see cref="IMcpToolSource"/> that returns a fixed set of tools — stands in for the
/// live MCP connection so the agent can be unit-tested in isolation.</summary>
internal sealed class FakeToolSource(params AITool[] tools) : IMcpToolSource
{
    public Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AITool>>(tools);
}
