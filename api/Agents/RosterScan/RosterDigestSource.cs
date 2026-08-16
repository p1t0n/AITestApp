using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Mcp;
using CvManager.Application.Search;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.RosterScan;

/// <summary>The intake sweep's seam over the <c>roster_digest_list</c> MCP tool: the runner pages
/// the roster deterministically (no model involved). Tests substitute a fake.</summary>
public interface IRosterDigestSource
{
    /// <summary>One page of digests, or null when the tool result is unreadable.</summary>
    Task<EmployeeDigestPage?> ListAsync(int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Invokes the real MCP tool through the roster-scan agent identity's tool source —
/// the same deterministic-invoke pattern as <see cref="Agents.McpShortlistSearch"/>.</summary>
public sealed class McpRosterDigestSource : IRosterDigestSource
{
    private const string ToolName = "roster_digest_list";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IMcpToolSource _toolSource;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIFunction? _tool;

    public McpRosterDigestSource(IMcpToolSource toolSource) => _toolSource = toolSource;

    public async Task<EmployeeDigestPage?> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var tool = await GetToolAsync(ct);
        var result = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["page"] = page,
            ["pageSize"] = pageSize,
        }, ct);
        return ToolResultPayload.Extract<EmployeeDigestPage>(
            result, obj => obj.ContainsKey("items") || obj.ContainsKey("Items"), Json);
    }

    private async Task<AIFunction> GetToolAsync(CancellationToken ct)
    {
        if (_tool is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_tool is { } stillCached)
            {
                return stillCached;
            }

            var tools = await _toolSource.GetToolsAsync(ct);
            _tool = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == ToolName)
                ?? throw new HttpRequestException($"The MCP server did not expose the {ToolName} tool.");
            return _tool;
        }
        finally
        {
            _gate.Release();
        }
    }
}
