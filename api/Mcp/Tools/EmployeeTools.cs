using System.ComponentModel;
using CvManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class EmployeeTools
{
    [McpServerTool(Name = "employee_list", ReadOnly = true, Destructive = false),
     Description("List all employees with a summary (name, title, location, email, current capacity)."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<EmployeeSummaryDto>> List(
        IEmployeeService employees, CancellationToken ct)
        => await employees.ListAsync(includeDrafts: false, ct);

    [McpServerTool(Name = "employee_get", ReadOnly = true, Destructive = false),
     Description("Get one employee, including all children (languages, availability, skills, qualifications, experiences), by id."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> Get(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.GetAsync(id, ct));

    [McpServerTool(Name = "employee_create", ReadOnly = false, Destructive = false),
     Description("Create an employee from root fields (children are managed by their own tools)."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Create(
        IEmployeeService employees,
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.CreateAsync(dto, ct));

    [McpServerTool(Name = "employee_create_draft", ReadOnly = false, Destructive = false),
     Description("Create a DRAFT employee from root fields (resume ingestion). Drafts are hidden from the roster, search, and staffing until a human promotes them; email may be empty if the source text has none. Returns the draft plus a duplicateWarning when a same-name employee already exists."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateDraft(
        IEmployeeService employees,
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.CreateDraftAsync(dto, ct));

    [McpServerTool(Name = "employee_update", ReadOnly = false, Destructive = false),
     Description("Update an employee's root fields by id."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "employee_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete an employee by id, including all children."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.DeleteAsync(id, ct));
}
