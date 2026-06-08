using System.ComponentModel;
using EmployeeManager.Application.Cv;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class CvTools
{
    [McpServerTool(Name = "cv_get", ReadOnly = true, Destructive = false),
     Description("Assemble and return an employee's full CV (all sections) by id. Returns data, not a PDF."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> Get(
        ICvService cv,
        [Description("Employee id (GUID).")] Guid employeeId,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => cv.BuildAsync(employeeId, ct));
}
