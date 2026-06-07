using System.ComponentModel;
using EmployeeManager.Application.Employees;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class ExperienceTools
{
    [McpServerTool(Name = "experience_add", ReadOnly = false, Destructive = false),
     Description("Add a work-experience record to an employee (company, title, dates, summary; null end date = current).")]
    public static Task<object> Add(
        IExperienceService experiences,
        [Description("Employee id (GUID).")] Guid employeeId,
        SaveExperienceDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "experience_update", ReadOnly = false, Destructive = false),
     Description("Update a work-experience record by id.")]
    public static Task<object> Update(
        IExperienceService experiences,
        [Description("Experience id (GUID).")] Guid id,
        SaveExperienceDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "experience_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete a work-experience record by id.")]
    public static Task<object> Delete(
        IExperienceService experiences,
        [Description("Experience id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.DeleteAsync(id, ct));
}
