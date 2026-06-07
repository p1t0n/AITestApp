using System.ComponentModel;
using EmployeeManager.Application.Employees;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class EmployeeTools
{
    [McpServerTool(Name = "employee_list", ReadOnly = true, Destructive = false),
     Description("List all employees with a summary (name, title, location, email, current capacity).")]
    public static async Task<IReadOnlyList<EmployeeSummaryDto>> List(
        IEmployeeService employees, CancellationToken ct)
        => await employees.ListAsync(ct);

    [McpServerTool(Name = "employee_get", ReadOnly = true, Destructive = false),
     Description("Get one employee, including all children (languages, availability, skills, qualifications, experiences), by id.")]
    public static Task<object> Get(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.GetAsync(id, ct));

    [McpServerTool(Name = "employee_create", ReadOnly = false, Destructive = false),
     Description("Create an employee from root fields (children are managed by their own tools).")]
    public static Task<object> Create(
        IEmployeeService employees,
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.CreateAsync(dto, ct));

    [McpServerTool(Name = "employee_update", ReadOnly = false, Destructive = false),
     Description("Update an employee's root fields by id.")]
    public static Task<object> Update(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "employee_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete an employee by id, including all children.")]
    public static Task<object> Delete(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync<object?>(async () =>
        {
            await employees.DeleteAsync(id, ct);
            return null;
        });
}
