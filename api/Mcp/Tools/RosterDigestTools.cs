using System.ComponentModel;
using ExpertToJob.Application.Search;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class RosterDigestTools
{
    [McpServerTool(Name = "roster_digest_list", ReadOnly = true, Destructive = false),
     Description(
         "Page through compact career digests of EVERY active employee — identity (employeeId, " +
         "name, title) plus the same narrative text semantic search indexes (professional summary " +
         "and per-role blocks with dates and achievement bullets, truncated for prompt use). Use " +
         "this to sweep the whole roster for bulk assessment (e.g. scoring every candidate against " +
         "one job description), one page per call. Do NOT use it to answer capability questions " +
         "('who has done X') — roster_semantic_search ranks by meaning; and do NOT use it for one " +
         "person's full detail — cv_get returns the complete CV. Input: page (1-based, default 1) " +
         "and pageSize (default 50, capped at 100); e.g. {\"page\": 2, \"pageSize\": 50}. The " +
         "result carries total, so callers can size a full sweep (pages = ceil(total/pageSize)). " +
         "Digests do NOT include availability, skill levels, languages, or contact data — only the " +
         "career narrative. Returns an empty items list past the last page."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> DigestList(
        IEmployeeDigestService digests,
        CancellationToken ct,
        [Description("1-based page number (default 1).")]
        int? page = null,
        [Description("Employees per page (default 50, capped at 100).")]
        int? pageSize = null)
        => McpToolExecutor.RunAsync(() => digests.ListAsync(page ?? 1, pageSize, ct));
}
