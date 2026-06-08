using System.ComponentModel;
using EmployeeManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class QualificationTools
{
    [McpServerTool(Name = "qualification_add", ReadOnly = false, Destructive = false),
     Description("Add a qualification (degree or certification) to an employee."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IQualificationService qualifications,
        [Description("Employee id (GUID).")] Guid employeeId,
        SaveQualificationDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "qualification_update", ReadOnly = false, Destructive = false),
     Description("Update a qualification by id."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IQualificationService qualifications,
        [Description("Qualification id (GUID).")] Guid id,
        SaveQualificationDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "qualification_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete a qualification by id."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IQualificationService qualifications,
        [Description("Qualification id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => qualifications.DeleteAsync(id, ct));
}
