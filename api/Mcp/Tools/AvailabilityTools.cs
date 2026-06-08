using System.ComponentModel;
using EmployeeManager.Application.Availability;
using EmployeeManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace EmployeeManager.Mcp.Tools;

[McpServerToolType]
public class AvailabilityTools
{
    [McpServerTool(Name = "availability_list", ReadOnly = true, Destructive = false),
     Description("List an employee's availability entries (capacity step function over time), ordered by effective-from date."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<AvailabilityEntryDto>> List(
        IAvailabilityService availability,
        [Description("Employee id (GUID).")] Guid employeeId,
        CancellationToken ct)
        => await availability.ListAsync(employeeId, ct);

    [McpServerTool(Name = "availability_add", ReadOnly = false, Destructive = false),
     Description("Add an availability entry (effective-from date + capacity percent 0-100) to an employee."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IAvailabilityService availability,
        [Description("Employee id (GUID).")] Guid employeeId,
        SaveAvailabilityEntryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "availability_update", ReadOnly = false, Destructive = false),
     Description("Update an availability entry by id."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IAvailabilityService availability,
        [Description("Availability entry id (GUID).")] Guid id,
        SaveAvailabilityEntryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "availability_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description("Delete an availability entry by id."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IAvailabilityService availability,
        [Description("Availability entry id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.DeleteAsync(id, ct));
}
