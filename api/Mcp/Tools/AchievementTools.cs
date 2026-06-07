using System.ComponentModel;
using EmployeeManager.Application.Employees;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class AchievementTools
{
    [McpServerTool(Name = "achievement_add", ReadOnly = false, Destructive = false),
     Description("Add an achievement bullet (order + text) to a work experience.")]
    public static Task<object> Add(
        IAchievementService achievements,
        [Description("Experience id (GUID).")] Guid experienceId,
        SaveAchievementDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.AddAsync(experienceId, dto, ct));

    [McpServerTool(Name = "achievement_update", ReadOnly = false, Destructive = false),
     Description("Update an achievement bullet (order / text) by id.")]
    public static Task<object> Update(
        IAchievementService achievements,
        [Description("Achievement id (GUID).")] Guid id,
        SaveAchievementDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "achievement_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete an achievement bullet by id.")]
    public static Task<object> Delete(
        IAchievementService achievements,
        [Description("Achievement id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => achievements.DeleteAsync(id, ct));
}
