using System.Text.Json;
using ExpertToJob.Agents.Mcp;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>The deterministic retrieval seam (P1T-117): the run service invokes the
/// <c>roster_shortlist_search</c> MCP tool directly with the extractor's requirement texts —
/// the model no longer picks tool arguments. Tests substitute a fake.</summary>
public interface IShortlistSearch
{
    /// <summary>Invokes the shortlist tool with the given requirements and the request's filters.
    /// Returns null when the tool result is unreadable.</summary>
    Task<ShortlistToolPayload?> SearchAsync(
        IReadOnlyList<string> requirements, ShortlistAgentRequest request, CancellationToken ct = default);
}

/// <summary>Invokes the real MCP tool through the shortlist agent identity's tool source. The
/// argument names/formats mirror what the model used to pass (the tool contract is unchanged).</summary>
public sealed class McpShortlistSearch : IShortlistSearch
{
    private const string ToolName = "roster_shortlist_search";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IMcpToolSource _toolSource;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIFunction? _tool;

    public McpShortlistSearch(IMcpToolSource toolSource) => _toolSource = toolSource;

    public async Task<ShortlistToolPayload?> SearchAsync(
        IReadOnlyList<string> requirements, ShortlistAgentRequest request, CancellationToken ct = default)
    {
        var tool = await GetToolAsync(ct);
        var arguments = new AIFunctionArguments { ["requirements"] = requirements };
        if (request.AvailableOn is { } availableOn)
        {
            arguments["availableOn"] = availableOn.ToString("yyyy-MM-dd");
        }

        if (request.SkillIds is { Length: > 0 } skillIds)
        {
            arguments["skillIds"] = skillIds;
        }

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            arguments["location"] = request.Location;
        }

        if (request.MinYears is { } minYears)
        {
            arguments["minYears"] = minYears;
        }

        if (request.TopK is { } topK)
        {
            arguments["topK"] = topK;
        }

        var result = await tool.InvokeAsync(arguments, ct);
        return ToolResultPayload.Extract<ShortlistToolPayload>(
            result, obj => obj.ContainsKey("results") || obj.ContainsKey("Results"), Json);
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
