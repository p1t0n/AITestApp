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
}
