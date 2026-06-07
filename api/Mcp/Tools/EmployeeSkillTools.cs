using System.ComponentModel;
using EmployeeManager.Application.Employees;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class EmployeeSkillTools
{
    [McpServerTool(Name = "employee_skill_add", ReadOnly = false, Destructive = false),
     Description("Add a catalog skill to an employee with a level and years of experience.")]
    public static Task<object> Add(
        IEmployeeSkillService skills,
        [Description("Employee id (GUID).")] Guid employeeId,
        SaveEmployeeSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "employee_skill_update", ReadOnly = false, Destructive = false),
     Description("Update an employee skill (level / years) by id.")]
    public static Task<object> Update(
        IEmployeeSkillService skills,
        [Description("Employee-skill id (GUID).")] Guid id,
        SaveEmployeeSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "employee_skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete an employee skill by id.")]
    public static Task<object> Delete(
        IEmployeeSkillService skills,
        [Description("Employee-skill id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.DeleteAsync(id, ct));
}
