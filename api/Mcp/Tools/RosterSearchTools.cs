using System.ComponentModel;
using CvManager.Application.Search;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class RosterSearchTools
{
    [McpServerTool(Name = "roster_semantic_search", ReadOnly = true, Destructive = false),
     Description(
         "Find employees by the MEANING of their experience — search their career narratives " +
         "(work summaries and achievements) semantically. Use this for capability questions like " +
         "'who has built real-time payments systems' or 'anyone with fintech + team-lead experience', " +
         "where the answer lives in prose rather than skill tags. For exact facts (specific skill " +
         "levels, availability dates, contact info) prefer the structured tools. Returns the best-" +
         "matching employees, each with a relevance score (0-1) and the evidence snippets that " +
         "matched; drill into cv_get for full detail. Returns an empty list when nothing is relevant."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> SemanticSearch(
        ISemanticSearchService search,
        [Description("Natural-language description of the capability or experience sought, " +
                     "e.g. 'led a real-time payments platform migration'.")]
        string query,
        CancellationToken ct,
        [Description("Optional: keep only employees available (capacity > 0) on this date (YYYY-MM-DD).")]
        DateOnly? availableOn = null,
        [Description("Optional: keep only employees who have ALL of these catalog skill ids (GUIDs).")]
        Guid[]? skillIds = null,
        [Description("Optional: keep only employees in this location (case-insensitive).")]
        string? location = null,
        [Description("Optional: minimum years of experience (applied to the required skills, or any skill if none given).")]
        decimal? minYears = null,
        [Description("Optional: max employees to return (default 5, capped at 20).")]
        int? topK = null)
    {
        var hasFilter = availableOn is not null
            || (skillIds is { Length: > 0 })
            || !string.IsNullOrWhiteSpace(location)
            || minYears is not null;

        var filters = hasFilter
            ? new SemanticSearchFilters(availableOn, skillIds, location, minYears)
            : null;

        return McpToolExecutor.RunAsync(() => search.SearchAsync(query, filters, topK, ct));
    }
}
