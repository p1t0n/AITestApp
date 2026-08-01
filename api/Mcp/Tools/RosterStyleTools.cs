using System.ComponentModel;
using CvManager.Application.Search;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class RosterStyleTools
{
    [McpServerTool(Name = "style_exemplar_search", ReadOnly = true, Destructive = false),
     Description(
         "Fetch strong-phrasing exemplars for CV bullet tailoring. Give it achievement ids taken " +
         "from cv_get and it returns, per bullet, the closest quantified achievement bullets from " +
         "OTHER employees' CVs, anonymized ([name]/[company] placeholders) — use them to imitate " +
         "phrasing style (action verb, metric, outcome) only, never to copy facts, names, or " +
         "numbers into a CV. Unknown ids are skipped; a bullet with no strong match nearby gets an " +
         "empty exemplar list."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> StyleExemplarSearch(
        IExemplarSearchService search,
        [Description("Achievement ids (GUIDs from cv_get) of the bullets being rewritten.")]
        Guid[] achievementIds,
        CancellationToken ct,
        [Description("Optional: exemplars per bullet (default 2, capped at 5).")]
        int? topKPerBullet = null)
        => McpToolExecutor.RunAsync(() => search.SearchAsync(achievementIds, topKPerBullet, ct));
}
