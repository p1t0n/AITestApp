using System.ComponentModel;
using CvManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class ExperienceTools
{
    [McpServerTool(Name = "experience_add", ReadOnly = false, Destructive = false),
     Description(
         "Add ONE JOB — a work-experience record — to a person: company, title, dates, optional " +
         "location and summary, plus (optionally, in the same call) its achievement bullets and " +
         "the catalog skills used in that role. Use it for 'add a work experience: Platform Lead " +
         "at FlowWorks since March 2019', or when staging a parsed resume's roles. A null endDate " +
         "means the role is current. Do NOT reach for a skill tool because a role mentions " +
         "technologies — pass their catalog ids in skillIds here (or link them later with " +
         "experience_skill_add); do NOT use it to add a bullet to an EXISTING role — " +
         "achievement_add; do NOT use it to change a role — experience_update by its id. Input: " +
         "employeeId + dto {company, title, location?, startDate (yyyy-MM-dd), endDate? " +
         "(yyyy-MM-dd or null), summary?, achievements[], skillIds[]}; e.g. {\"employeeId\": " +
         "\"7b2e8d3a-1111-2222-3333-444455556666\", \"dto\": {\"company\": \"FlowWorks\", " +
         "\"title\": \"Platform Lead\", \"startDate\": \"2019-03-01\", \"endDate\": " +
         "null, \"achievements\": [{\"order\": 1, \"text\": \"Cut deploy time by 40%\"}], " +
         "\"skillIds\": []}}. Returns the created experience with its own id (and its bullets' " +
         "ids) — the handle for achievement_add and experience_skill_add."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IExperienceService experiences,
        [Description("Employee id (GUID) whose job this is.")] Guid employeeId,
        [Description("company, title required; startDate yyyy-MM-dd; endDate yyyy-MM-dd or null " +
                     "for a current role; achievements: [{order, text}] bullets; skillIds: " +
                     "EXISTING catalog skill ids (GUIDs) used in this role. Pass empty arrays if " +
                     "the source text gives none — never invent bullets or skills.")]
        SaveExperienceDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "experience_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE one existing job by EXPERIENCE id (from employee_get or cv_get) — company, title, " +
         "dates, location, summary, and its achievement/skill sets. Use it for 'that role ended in " +
         "June', 'fix the company name'. This is a full replace: the achievements and skillIds you " +
         "send become the complete set, so omitting them clears them — send the existing bullets " +
         "back if they should stay. Do NOT use it to add another job — experience_add with the " +
         "employee id; do NOT use it to edit a single bullet — achievement_update is safer and " +
         "cannot lose the others. Input: id + dto (same shape as experience_add); e.g. {\"id\": " +
         "\"5d5d5d5d-1111-2222-3333-444455556666\", \"dto\": {\"company\": " +
         "\"FlowWorks\", \"title\": \"Platform Lead\", \"startDate\": \"2019-03-01\", " +
         "\"endDate\": \"2026-06-30\", \"achievements\": [], \"skillIds\": []}}. Returns " +
         "the updated experience."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IExperienceService experiences,
        [Description("Experience id (GUID) from employee_get / cv_get — not the employee id.")]
        Guid id,
        [Description("The experience AFTER the edit. Full replace: achievements and skillIds " +
                     "become the complete sets, so include the ones that should survive.")]
        SaveExperienceDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "experience_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: delete one job by experience id, together with its achievement bullets and " +
         "skill links (and their search-index chunks). Use it only to remove a role that should " +
         "not be on the record. Do NOT use it to end an ongoing role — experience_update with an " +
         "endDate keeps the history; do NOT use it to drop one bullet — achievement_delete. Input: " +
         "id; e.g. {\"id\": \"5d5d5d5d-1111-2222-3333-444455556666\"}. Requires the admin " +
         "scope; idempotent. Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IExperienceService experiences,
        [Description("Experience id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experiences.DeleteAsync(id, ct));
}
