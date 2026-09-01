using System.ComponentModel;
using ExpertToJob.Application.Search;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class RosterShortlistTools
{
    [McpServerTool(Name = "roster_shortlist_search", ReadOnly = true, Destructive = false),
     Description(
         "Shortlist and RANK candidates against a job description by breaking it into 3-8 short " +
         "capability requirements (e.g. 'event streaming with Kafka', 'led a platform team'). Each " +
         "requirement is searched separately over the career narratives, and candidates are ranked " +
         "coverage-first: someone matching 4 of 5 requirements outranks a perfect match on just " +
         "one. Use it whenever a role, a pasted job description or a list of must-haves is the " +
         "input and the ask is 'who are the top candidates', 'rank our matches', 'who covers the " +
         "most of these' — with per-requirement evidence. Do NOT use it for a single free-form " +
         "capability question — roster_semantic_search does that; do NOT use it to score EVERY " +
         "expert one by one — roster_digest_list pages the whole roster for a bulk sweep; do NOT " +
         "use it for phrasing examples — style_exemplar_search; do NOT use it as a roster listing " +
         "— expert_list. Input: requirements (3-8 short phrases, NOT the raw JD text) plus " +
         "optional filters (availableOn, skillIds, location, minYears, topK); e.g. " +
         "{\"requirements\": [\"Kafka event streaming\", \"Kubernetes operations\", \"team " +
         "leadership\"], \"topK\": 10}. Returns each candidate's coverage (matched count / " +
         "total) and per-requirement evidence — matched or not, with the best snippet and " +
         "similarity when matched; NOT full CVs (drill into cv_get) and no interview questions. " +
         "Returns an empty list when nothing is relevant."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> ShortlistSearch(
        IShortlistSearchService search,
        [Description("3-8 short capability requirements distilled from the job description, one " +
                     "phrase each, e.g. ['real-time payments', 'Kubernetes operations', 'team leadership'].")]
        string[] requirements,
        CancellationToken ct,
        [Description("Optional: keep only experts available (capacity > 0) on this date (YYYY-MM-DD).")]
        DateOnly? availableOn = null,
        [Description("Optional: keep only experts who have ALL of these catalog skill ids (GUIDs).")]
        Guid[]? skillIds = null,
        [Description("Optional: keep only experts in this location (case-insensitive).")]
        string? location = null,
        [Description("Optional: minimum years of experience (applied to the required skills, or any skill if none given).")]
        decimal? minYears = null,
        [Description("Optional: max candidates to return (default 10, capped at 20).")]
        int? topK = null)
    {
        var hasFilter = availableOn is not null
            || (skillIds is { Length: > 0 })
            || !string.IsNullOrWhiteSpace(location)
            || minYears is not null;

        var filters = hasFilter
            ? new SemanticSearchFilters(availableOn, skillIds, location, minYears)
            : null;

        return McpToolExecutor.RunAsync(() => search.SearchAsync(requirements, filters, topK, ct));
    }
}
