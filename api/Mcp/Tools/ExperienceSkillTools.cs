using System.ComponentModel;
using EmployeeManager.Application.Employees;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class ExperienceSkillTools
{
    [McpServerTool(Name = "experience_skill_add", ReadOnly = false, Destructive = false),
     Description("Link a catalog skill to a work experience (evidence trail of skills used in that role).")]
    public static Task<object> Add(
        IExperienceSkillService links,
        [Description("Experience id (GUID).")] Guid experienceId,
        [Description("Catalog skill id (GUID).")] Guid skillId,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => links.AddAsync(experienceId, skillId, ct));

    [McpServerTool(Name = "experience_skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Remove a skill link from a work experience by link id.")]
    public static Task<object> Delete(
        IExperienceSkillService links,
        [Description("Experience-skill link id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => links.DeleteAsync(id, ct));
}
