using Microsoft.Extensions.AI;

namespace CvManager.Agents.Mcp;

/// <summary>
/// Supplies the MCP tools an agent may use, as Microsoft.Extensions.AI <see cref="AITool"/>s.
/// Abstracted so agents can be unit-tested with a fake tool set (no live MCP server).
/// </summary>
public interface IMcpToolSource
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default);
}
