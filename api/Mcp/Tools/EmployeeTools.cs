using System.ComponentModel;
using CvManager.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CvManager.Mcp.Tools;

[McpServerToolType]
public class EmployeeTools
{
    [McpServerTool(Name = "employee_list", ReadOnly = true, Destructive = false),
     Description(
         "List every active employee as one flat roster row each — id, first/last name, " +
         "title, location, email, current capacity percent and status. No paging, no filters, no " +
         "narrative text. Use it when the roster itself is the answer: 'list everyone with their " +
         "emails', 'how many employees do we have', 'show each person and their location'. Do NOT " +
         "use it for capability questions ('who has built X') — roster_semantic_search ranks by " +
         "meaning; do NOT use it to rank people against a job description or its must-haves — " +
         "roster_shortlist_search returns coverage and per-requirement evidence; do NOT use it to " +
         "sweep career narratives in bulk — roster_digest_list pages compact digests; do NOT use " +
         "it for one person — employee_get returns their child records, cv_get the assembled CV. " +
         "Input: none; e.g. {}. Rows carry NO skills, languages, qualifications, experiences, " +
         "availability history or CV prose, and draft employees are excluded."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<EmployeeSummaryDto>> List(
        IEmployeeService employees, CancellationToken ct)
        => await employees.ListAsync(includeDrafts: false, ct);

    [McpServerTool(Name = "employee_get", ReadOnly = true, Destructive = false),
     Description(
         "Get ONE employee by id with every child record: languages, the full availability step " +
         "function (effectiveFrom + capacityPercent entries), skills with level and years, " +
         "qualifications and experiences — each child carrying its own id for follow-up writes. " +
         "Use it when you already have the employeeId and need that person's exact structured " +
         "facts (a skill's years, an availability date, contact fields, draft status). Do NOT use " +
         "it to FIND people — roster_semantic_search answers capability questions, " +
         "roster_shortlist_search ranks against a job description; do NOT use it when the CV is " +
         "what's wanted (prose to review, render or quote verbatim, achievement-bullet ids) — " +
         "cv_get assembles the CV; do NOT loop it over the roster — employee_list for summary " +
         "rows, roster_digest_list for narrative digests; and do NOT use it when the request is " +
         "to CHANGE something — it only reads: employee_update edits root fields like title or " +
         "email, availability_add records a capacity change, and the child *_add / *_update tools " +
         "edit skills, languages, qualifications and experiences. Input: id — the employee GUID; " +
         "e.g. " +
         "{\"id\": \"7b2e8d3a-1111-2222-3333-444455556666\"}. An unknown id returns a " +
         "not_found error, never an empty employee. Returns stored data only — no CV layout, no " +
         "PDF, no relevance scores."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> Get(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.GetAsync(id, ct));

    [McpServerTool(Name = "employee_create", ReadOnly = false, Destructive = false),
     Description(
         "Create ONE ACTIVE employee from root fields only — first/last name, title, email, and " +
         "optional phone, location, summary, photoUrl. The new employee is immediately visible to " +
         "the roster, search and staffing. Use it when a human is adding a known colleague. Do " +
         "NOT use it for a resume an agent just parsed — employee_create_draft stages a hidden " +
         "draft for human promotion, which is the ingestion path; do NOT use it to change an " +
         "existing person — employee_update; do NOT try to pass skills, languages, availability, " +
         "qualifications or experiences here — they are separate tools (employee_skill_add, " +
         "language_add, availability_add, qualification_add, experience_add) run after this one " +
         "returns the new id. Input: dto with the root fields; e.g. {\"dto\": {\"firstName\": " +
         "\"Jane\", \"lastName\": \"Doe\", \"title\": \"Senior Engineer\", \"email\": " +
         "\"jane@example.com\", \"location\": \"Berlin\"}}. The email must be unique among " +
         "ACTIVE employees — a clash returns a conflict error, malformed fields a validation error " +
         "with per-field detail. Returns the created employee (with its new id) and NO children."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Create(
        IEmployeeService employees,
        [Description("Root fields of the new employee. Required: firstName, lastName, title, " +
                     "email (unique). Optional: phone, location, summary, photoUrl.")]
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.CreateAsync(dto, ct));

    [McpServerTool(Name = "employee_create_draft", ReadOnly = false, Destructive = false),
     Description(
         "Stage ONE DRAFT employee from root fields — the resume-ingestion path. Drafts are hidden " +
         "from the roster, search, staffing and every list until a HUMAN promotes them, so this is " +
         "the tool for anything extracted from a pasted CV or resume rather than confirmed by a " +
         "person. Unlike employee_create it accepts an EMPTY email, because a resume often has " +
         "none — never invent one. Do NOT use it when a human is entering a colleague they know " +
         "— employee_create makes an active employee; do NOT use it to amend an existing draft — " +
         "employee_update by id. Input: dto with whatever the source text actually supports; e.g. " +
         "{\"dto\": {\"firstName\": \"Jane\", \"lastName\": \"Doe\", \"title\": " +
         "\"Backend Engineer\", \"email\": \"\"}}. Returns the draft (with its new id) plus a " +
         "duplicateWarning when a same-name employee already exists — surface that warning, do not " +
         "silently merge. Children are NOT created here; add them by id afterwards."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateDraft(
        IEmployeeService employees,
        [Description("Root fields extracted from the source text. email may be an empty string " +
                     "when the resume has none — never fabricate one.")]
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.CreateDraftAsync(dto, ct));

    [McpServerTool(Name = "employee_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE an existing employee's root fields by id — name, title, email, phone, location, " +
         "summary, photoUrl. Use it for edits like 'change their title to Staff Engineer', 'fix " +
         "their email', 'update their professional summary'. This is a full replace of the root " +
         "fields, so send every field you want kept, not just the changed one. Do NOT use " +
         "employee_get for a change — that only reads; do NOT use it for children: skills go " +
         "through employee_skill_add/update, capacity through availability_add (capacity is a " +
         "dated step function, not a root field), and likewise language_*, qualification_*, " +
         "experience_*; do NOT use it to create anyone — employee_create / employee_create_draft. " +
         "Input: id (employee GUID) + dto; e.g. {\"id\": " +
         "\"7b2e8d3a-1111-2222-3333-444455556666\", \"dto\": {\"firstName\": \"Jane\", " +
         "\"lastName\": \"Doe\", \"title\": \"Staff Engineer\", \"email\": " +
         "\"jane@example.com\"}}. An unknown id returns not_found; a duplicate email conflict. " +
         "Returns the updated employee, children untouched; it never promotes a draft."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        [Description("The employee's root fields AFTER the edit — a full replace, so include the " +
                     "fields that stay the same as well.")]
        SaveEmployeeDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.UpdateAsync(id, dto, ct));

    [McpServerTool(Name = "employee_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE, irreversible: delete one employee by id together with every child — " +
         "languages, availability, skills, qualifications, experiences and their achievement " +
         "bullets — and their search-index chunks. Use it only on an explicit human instruction to " +
         "remove a person's record. Do NOT use it to take someone off staffing consideration " +
         "(availability_add with capacityPercent 0 expresses that), to drop one child record (the " +
         "child's own *_delete tool does), or to discard a bad draft you would rather correct " +
         "(employee_update). Input: id (employee GUID); e.g. {\"id\": " +
         "\"7b2e8d3a-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent — " +
         "deleting an already-deleted id is not an error. Returns no employee data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IEmployeeService employees,
        [Description("Employee id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => employees.DeleteAsync(id, ct));
}
