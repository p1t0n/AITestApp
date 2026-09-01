using System.ComponentModel;
using ExpertToJob.Application.Search;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class RosterStyleTools
{
    [McpServerTool(Name = "style_exemplar_search", ReadOnly = true, Destructive = false),
     Description(
         "Fetch strong-PHRASING exemplars for CV bullet writing, in one of two modes — supply " +
         "EXACTLY ONE of achievementIds or theme, never both, never neither. Id mode: given " +
         "achievement ids, returns per bullet the closest quantified achievement bullets from " +
         "OTHER employees' CVs. Theme mode: given a free-text theme with no bullet to name, " +
         "returns the closest quantified achievement bullets against that theme across the whole " +
         "roster. Both modes return anonymized results ([name]/[company] placeholders). Use this tool " +
         "whenever the ask is about WORDING rather than people — 'examples of strongly phrased " +
         "achievement bullets about cost reduction', 'well-written phrasing samples for " +
         "describing platform migration work', 'exemplar bullets that quantify impact with " +
         "metrics' — and imitate phrasing only (action verb, metric shape, outcome), never " +
         "copying an exemplar's facts, names or numbers into a CV. This — not " +
         "roster_semantic_search — is the tool for a phrasing request, whether or not a specific " +
         "bullet is named: pass achievementIds when tailoring a CV whose bullets you already have " +
         "(from cv_get); pass theme when the ask names a kind of achievement instead " +
         "('cost-reduction bullets', 'platform migration phrasing') with no bullet in hand. Do " +
         "NOT use it to find or rank PEOPLE — roster_semantic_search answers capability " +
         "questions, roster_shortlist_search ranks against a job description; do NOT use it to " +
         "read one person's own bullets — cv_get returns their CV (and the achievementIds this " +
         "tool takes). Input: either achievementIds (GUIDs from cv_get) or theme (free text), " +
         "plus optional topKPerBullet; e.g. " +
         "{\"achievementIds\": [\"5d5d5d5d-1111-2222-3333-444455556666\"], " +
         "\"topKPerBullet\": 2} or {\"theme\": \"cost reduction\", \"topKPerBullet\": 3}. Both " +
         "or neither supplied is a validation error. Unknown ids are skipped; a bullet or theme " +
         "with no strong match nearby gets an empty exemplar list. Returns exemplar bullet text " +
         "with similarity only — no employee identities and no rewritten bullet (the caller does " +
         "the rewriting)."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> StyleExemplarSearch(
        IExemplarSearchService search,
        CancellationToken ct,
        [Description("Achievement ids (GUIDs from cv_get) of the bullets being rewritten. " +
            "Mutually exclusive with theme — supply exactly one.")]
        Guid[]? achievementIds = null,
        [Description("Free-text theme to find phrasing exemplars for, when no specific bullet " +
            "is in hand (e.g. \"cost reduction\", \"platform migration\"). Mutually exclusive " +
            "with achievementIds — supply exactly one.")]
        string? theme = null,
        [Description("Optional: exemplars per bullet, or per theme in theme mode (default 2, capped at 5).")]
        int? topKPerBullet = null)
        => McpToolExecutor.RunAsync(() => search.SearchAsync(achievementIds, theme, topKPerBullet, ct));
}
