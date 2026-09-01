using System.ComponentModel;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class ExperienceSkillTools
{
    [McpServerTool(Name = "experience_skill_add", ReadOnly = false, Destructive = false),
     Description(
         "Link an EXISTING catalog skill to ONE JOB — the evidence trail of which skills were " +
         "actually used in that role ('she used Kubernetes at FlowWorks'). It joins an experience " +
         "id to a catalog skill id and carries no level or years. Do NOT use it to say the PERSON " +
         "has the skill overall — expert_skill_add holds their level and years, and the two are " +
         "complementary: the role link is evidence, the expert skill is the claim; do NOT use it " +
         "to create a skill — skill_create adds it to the catalog first, skill_list finds its id. " +
         "Input: experienceId + skillId; e.g. {\"experienceId\": " +
         "\"5d5d5d5d-1111-2222-3333-444455556666\", \"skillId\": " +
         "\"8a8a8a8a-1111-2222-3333-444455556666\"}. An unknown id returns not_found, a repeat " +
         "link conflict. Returns the created link row with its own id."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IExperienceSkillService links,
        [Description("Experience id (GUID) — the role the skill was used in.")] Guid experienceId,
        [Description("EXISTING catalog skill id (GUID) from skill_list.")] Guid skillId,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => links.AddAsync(experienceId, skillId, ct));

    [McpServerTool(Name = "experience_skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: remove one skill-to-role link by LINK id (the experienceSkill row's own id " +
         "from expert_get, not the experience id and not the skill id). The role, the catalog " +
         "skill and the person's own skill entry are all untouched. Do NOT use it to take a skill " +
         "off the person — expert_skill_delete. Input: id; e.g. {\"id\": " +
         "\"1e1e1e1e-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IExperienceSkillService links,
        [Description("Experience-skill link id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => links.DeleteAsync(id, ct));
}
