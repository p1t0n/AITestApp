using System.ComponentModel;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class AchievementTools
{
    [McpServerTool(Name = "achievement_add", ReadOnly = false, Destructive = false),
     Description(
         "Append ONE achievement bullet to an existing job — the quantified 'what I accomplished' " +
         "line under a work experience. It hangs off the EXPERIENCE id (from expert_get or " +
         "cv_get), NOT the expert id, because a bullet belongs to a role. Use it for 'append " +
         "\"Cut deploy time by 40%\" to that experience'. Do NOT use it when the job itself does " +
         "not exist yet — experience_add creates the role and can carry its bullets in one call; " +
         "do NOT use it to reword an existing bullet — achievement_update by the achievement id; " +
         "do NOT use it to write a tailored rewrite into a CV — rewrites are proposed to a human, " +
         "and style_exemplar_search only supplies phrasing exemplars. Input: experienceId + dto " +
         "{order, text}; e.g. {\"experienceId\": \"5d5d5d5d-1111-2222-3333-444455556666\", " +
         "\"dto\": {\"order\": 1, \"text\": \"Cut deploy time by 40%\"}}. Returns the " +
         "created bullet with its achievementId — the key style_exemplar_search takes."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IAchievementService achievements,
        [Description("Experience id (GUID) — the ROLE this bullet belongs to, not the expert id.")]
        Guid experienceId,
        [Description("order: 1-based display position among that role's bullets; text: the bullet " +
                     "itself, taken verbatim from the source — never invented or embellished.")]
        SaveAchievementDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.AddAsync(experienceId, dto, ct));

    [McpServerTool(Name = "achievement_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE one achievement bullet — its text or its display order — by ACHIEVEMENT id (from " +
         "cv_get or expert_get). Use it to fix wording or reorder bullets within a role. Do NOT " +
         "use it to add a bullet — achievement_add with the experience id; do NOT use " +
         "experience_update for a single bullet, since its achievements array is a full replace " +
         "and can silently drop the others. Input: id + dto {order, text}; e.g. {\"id\": " +
         "\"2b2b2b2b-1111-2222-3333-444455556666\", \"dto\": {\"order\": 2, \"text\": " +
         "\"Cut deploy time by 40% across 12 services\"}}. Returns the updated bullet."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IAchievementService achievements,
        [Description("Achievement id (GUID) from cv_get / expert_get — not the experience id.")]
        Guid id,
        [Description("order and text AFTER the edit; both are replaced.")]
        SaveAchievementDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "achievement_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: delete one achievement bullet by achievement id (from cv_get or " +
         "expert_get), leaving its role and the other bullets intact. Do NOT use it to reword " +
         "— achievement_update. Input: id; e.g. {\"id\": " +
         "\"2b2b2b2b-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IAchievementService achievements,
        [Description("Achievement id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.DeleteAsync(id, ct));
}
