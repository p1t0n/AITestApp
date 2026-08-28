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
         "rows, roster_digest_list for narrative digests. Input: id — the employee GUID; e.g. " +
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
