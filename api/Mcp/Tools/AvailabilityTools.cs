using System.ComponentModel;
using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class AvailabilityTools
{
    [McpServerTool(Name = "availability_list", ReadOnly = true, Destructive = false),
     Description(
         "List ONE employee's availability entries — the capacity step function over time, ordered " +
         "by effectiveFrom: each entry says 'from this date, this person is at N% capacity' and " +
         "holds until the next entry. Use it to answer 'what is their availability', 'when do they " +
         "free up', or to read an entry's id before changing it. Do NOT use it to find who is free " +
         "on a date — roster_semantic_search and roster_shortlist_search take an availableOn " +
         "filter and search the whole roster; do NOT use it for the current headline number alone " +
         "— employee_list and employee_get already carry currentCapacityPercent. Input: " +
         "employeeId; e.g. {\"employeeId\": \"7b2e8d3a-1111-2222-3333-444455556666\"}. " +
         "Returns the dated steps only — no name, no skills, no CV."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<AvailabilityEntryDto>> List(
        IAvailabilityService availability,
        [Description("Employee id (GUID).")] Guid employeeId,
        CancellationToken ct)
        => await availability.ListAsync(employeeId, ct);

    [McpServerTool(Name = "availability_add", ReadOnly = false, Destructive = false),
     Description(
         "Record a CAPACITY CHANGE for one person from a given date — 'set her to 50% from " +
         "2026-10-01', 'he is fully free from March', 'book him out (0%) starting Monday'. " +
         "Availability is a step function, so this ADDS a step rather than overwriting a field: " +
         "the new percent holds from effectiveFrom until a later entry supersedes it, and past " +
         "entries stay as history. Do NOT look for a capacity field on the employee — " +
         "employee_update does not have one; do NOT use it to correct a step you just entered " +
         "wrongly — availability_update by the entry id (availability_list has them); do NOT use " +
         "it to remove someone from the roster — that is employee_delete, and 0% is usually what " +
         "is meant. Input: employeeId + dto {effectiveFrom (yyyy-MM-dd), capacityPercent (0-100)}; " +
         "e.g. {\"employeeId\": \"7b2e8d3a-1111-2222-3333-444455556666\", \"dto\": " +
         "{\"effectiveFrom\": \"2026-10-01\", \"capacityPercent\": 50}}. A second entry on " +
         "the same date returns conflict. Returns the created entry."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IAvailabilityService availability,
        [Description("Employee id (GUID) whose capacity changes.")] Guid employeeId,
        [Description("effectiveFrom: the date the new capacity starts, yyyy-MM-dd; " +
                     "capacityPercent: integer 0-100 (0 = fully booked, 100 = fully available).")]
        SaveAvailabilityEntryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "availability_update", ReadOnly = false, Destructive = false),
     Description(
         "CORRECT one existing availability step — its date or its percent — addressed by the " +
         "ENTRY id (from availability_list or employee_get), not the employee id. Use it when a " +
         "step was entered wrongly ('that 50% should start on the 15th'). Do NOT use it to record " +
         "a NEW change from a new date — availability_add appends a step and keeps the history, " +
         "which is what a genuine capacity change is. Input: id + dto {effectiveFrom (yyyy-MM-dd), " +
         "capacityPercent (0-100)} (full replace); e.g. {\"id\": " +
         "\"6f6f6f6f-1111-2222-3333-444455556666\", \"dto\": {\"effectiveFrom\": " +
         "\"2026-10-15\", \"capacityPercent\": 50}}. Returns the updated entry."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IAvailabilityService availability,
        [Description("Availability entry id (GUID) from availability_list — not the employee id.")]
        Guid id,
        [Description("effectiveFrom (yyyy-MM-dd) and capacityPercent (0-100) AFTER the correction.")]
        SaveAvailabilityEntryDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "availability_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: remove one availability step by entry id (from availability_list). Deleting " +
         "a step makes the previous step's capacity extend forward, which changes who looks " +
         "available. Do NOT use it for a real capacity change — availability_add appends a step " +
         "and keeps the history; do NOT use it to fix a wrong date or percent — " +
         "availability_update. Use this only to erase an entry that should never have existed. " +
         "Input: id; " +
         "e.g. {\"id\": \"6f6f6f6f-1111-2222-3333-444455556666\"}. Requires the admin scope; " +
         "idempotent. Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IAvailabilityService availability,
        [Description("Availability entry id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => availability.DeleteAsync(id, ct));
}
