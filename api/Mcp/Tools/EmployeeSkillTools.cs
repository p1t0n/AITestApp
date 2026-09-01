using System.ComponentModel;
using ExpertToJob.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class EmployeeSkillTools
{
    [McpServerTool(Name = "employee_skill_add", ReadOnly = false, Destructive = false),
     Description(
         "Attach an EXISTING catalog skill to ONE PERSON, with their proficiency level and years " +
         "of experience. This is the tool for 'X knows Y', 'mark that she has 5 years of " +
         "PostgreSQL', 'add Kubernetes at Advanced level to this employee'. It does NOT create the " +
         "skill: the skill must already exist in the catalog — look its id up with skill_list, and " +
         "if the catalog genuinely lacks it, skill_create adds it to the catalog FIRST (that tool " +
         "touches no person). Do NOT use it to record a skill used in one specific ROLE — " +
         "experience_skill_add links a catalog skill to a work experience; do NOT use it to change " +
         "an existing level or years — employee_skill_update by the employee-skill id from " +
         "employee_get. Input: employeeId + dto {skillId, level, yearsExperience}; e.g. " +
         "{\"employeeId\": \"7b2e8d3a-1111-2222-3333-444455556666\", \"dto\": {\"skillId\": " +
         "\"8a8a8a8a-1111-2222-3333-444455556666\", \"level\": \"Advanced\", " +
         "\"yearsExperience\": 5}}. An unknown skillId returns not_found and a skill the person " +
         "already has returns conflict — both are self-correctable. Returns the created " +
         "employee-skill row, not the employee."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        IEmployeeSkillService skills,
        [Description("Employee id (GUID) — the person gaining the skill.")] Guid employeeId,
        [Description("skillId: an EXISTING catalog skill id (GUID) from skill_list; level: one of " +
                     "Beginner, Intermediate, Advanced, Expert; yearsExperience: decimal years.")]
        SaveEmployeeSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "employee_skill_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE one person's existing skill entry — its level, years, or which catalog skill it " +
         "points at — addressed by the EMPLOYEE-SKILL id (the row's own id from employee_get or " +
         "cv_get), not by the employee id and not by the catalog skill id. Use it for 'raise her " +
         "PostgreSQL to Expert', 'correct the years on that skill'. Do NOT use it to give someone " +
         "a skill they do not have yet — employee_skill_add; do NOT use it to rename the skill " +
         "itself for everyone — skill_update edits the catalog entry. Input: id + dto {skillId, " +
         "level, yearsExperience} (full replace); e.g. {\"id\": " +
         "\"3c3c3c3c-1111-2222-3333-444455556666\", \"dto\": {\"skillId\": " +
         "\"8a8a8a8a-1111-2222-3333-444455556666\", \"level\": \"Expert\", " +
         "\"yearsExperience\": 7}}. Returns the updated row only."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IEmployeeSkillService skills,
        [Description("Employee-skill id (GUID) — the person's skill ROW, from employee_get; not " +
                     "the employee id and not the catalog skill id.")]
        Guid id,
        [Description("level: Beginner | Intermediate | Advanced | Expert; yearsExperience: " +
                     "decimal years; skillId: the catalog skill this row points at.")]
        SaveEmployeeSkillDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "employee_skill_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: remove one skill from ONE PERSON, addressed by the employee-skill row id " +
         "(from employee_get). The catalog skill itself is untouched and every other employee keeps " +
         "it. Do NOT use it to remove a skill from the catalog for everyone — skill_delete does " +
         "that; do NOT use it to lower a level — employee_skill_update. Input: id; e.g. {\"id\": " +
         "\"3c3c3c3c-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IEmployeeSkillService skills,
        [Description("Employee-skill id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => skills.DeleteAsync(id, ct));
}
