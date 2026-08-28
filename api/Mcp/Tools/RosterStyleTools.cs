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
         "Fetch strong-PHRASING exemplars for CV bullet writing: given achievement ids, it " +
         "returns per bullet the closest quantified achievement bullets from OTHER employees' " +
         "CVs, anonymized ([name]/[company] placeholders). Use it whenever the ask is about " +
         "WORDING rather than people — 'examples of strongly phrased achievement bullets', " +
         "'well-written phrasing samples for describing platform migration work', 'exemplar " +
         "bullets that quantify impact with metrics' — and imitate phrasing only (action verb, " +
         "metric shape, outcome), never copying an exemplar's facts, names or numbers into a CV. " +
         "This — not roster_semantic_search — is the tool for a phrasing request; it needs " +
         "achievement ids, so pass the ids of the bullets in play (from the CV being tailored). " +
         "Do NOT use it to find or rank PEOPLE — roster_semantic_search answers capability " +
         "questions, roster_shortlist_search ranks against a job description; do NOT use it to " +
         "read one person's own bullets — cv_get returns their CV (and the achievementIds this " +
         "tool takes). Input: achievementIds (GUIDs from cv_get — the bullets being rewritten) " +
         "and optional topKPerBullet; e.g. " +
         "{\"achievementIds\": [\"5d5d5d5d-1111-2222-3333-444455556666\"], " +
         "\"topKPerBullet\": 2}. Unknown ids are skipped; a bullet with no strong match nearby " +
         "gets an empty exemplar list. Returns exemplar bullet text with similarity only — no " +
         "employee identities and no rewritten bullet (the caller does the rewriting)."),
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
