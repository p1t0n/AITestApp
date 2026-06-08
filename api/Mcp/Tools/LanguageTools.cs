using System.ComponentModel;
using EmployeeManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class LanguageTools
{
    [McpServerTool(Name = "language_add", ReadOnly = false, Destructive = false),
     Description("Add a spoken language (with proficiency level) to an employee."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        ILanguageService languages,
        [Description("Employee id (GUID).")] Guid employeeId,
        SaveSpokenLanguageDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "language_update", ReadOnly = false, Destructive = false),
     Description("Update a spoken language by id."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        ILanguageService languages,
        [Description("Spoken-language id (GUID).")] Guid id,
        SaveSpokenLanguageDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "language_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete a spoken language by id."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        ILanguageService languages,
        [Description("Spoken-language id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.DeleteAsync(id, ct));
}
