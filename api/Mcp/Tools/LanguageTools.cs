using System.ComponentModel;
using CvManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class LanguageTools
{
    [McpServerTool(Name = "language_add", ReadOnly = false, Destructive = false),
     Description(
         "Record that ONE PERSON speaks a language, at a proficiency level. Use it for 'she speaks " +
         "German at Professional level', 'record his Spanish'. Languages are free text (they are " +
         "not part of the skill catalog), so no lookup call is needed first. Do NOT use it for a " +
         "programming language — that is a catalog skill: employee_skill_add (with skill_create " +
         "first if the catalog lacks it); do NOT use it to change an existing entry's level — " +
         "language_update by the spoken-language id from employee_get. Input: employeeId + dto " +
         "{language, level}; e.g. {\"employeeId\": \"7b2e8d3a-1111-2222-3333-444455556666\", " +
         "\"dto\": {\"language\": \"German\", \"level\": \"Professional\"}}. Returns the " +
         "created language row, not the employee."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Add(
        ILanguageService languages,
        [Description("Employee id (GUID) — the person who speaks it.")] Guid employeeId,
        [Description("language: free-text name, e.g. \"German\" (NOT a programming language); " +
                     "level: one of Basic, Conversational, Professional, Fluent, Native.")]
        SaveSpokenLanguageDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.AddAsync(employeeId, dto, ct));

    [McpServerTool(Name = "language_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE one existing spoken-language entry — its name or level — addressed by the " +
         "SPOKEN-LANGUAGE row id (from employee_get), not the employee id. Use it for 'her German " +
         "is Fluent, not Professional'. Do NOT use it to add a language the person does not have " +
         "yet — language_add with the employee id. Input: id + dto {language, level} (full " +
         "replace); e.g. {\"id\": \"4d4d4d4d-1111-2222-3333-444455556666\", \"dto\": " +
         "{\"language\": \"German\", \"level\": \"Fluent\"}}. Returns the updated row."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        ILanguageService languages,
        [Description("Spoken-language row id (GUID) from employee_get — not the employee id.")]
        Guid id,
        [Description("language and level AFTER the edit; level is one of Basic, Conversational, " +
                     "Professional, Fluent, Native.")]
        SaveSpokenLanguageDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "language_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE: remove one spoken-language entry from a person, by the spoken-language row " +
         "id (from employee_get). Do NOT use it to correct a level — language_update; the employee " +
         "and every other child record are untouched. Input: id; e.g. {\"id\": " +
         "\"4d4d4d4d-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent. " +
         "Returns no data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        ILanguageService languages,
        [Description("Spoken-language id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => languages.DeleteAsync(id, ct));
}
