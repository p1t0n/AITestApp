using System.ComponentModel;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Tools;

[McpServerToolType]
public class ExpertTools
{
    [McpServerTool(Name = "expert_list", ReadOnly = true, Destructive = false),
     Description(
         "List every active expert as one flat roster row each — id, first/last name, " +
         "title, location, email, current capacity percent and status. No paging, no filters, no " +
         "narrative text. Use it when the roster itself is the answer: 'list everyone with their " +
         "emails', 'how many experts do we have', 'show each person and their location'. Do NOT " +
         "use it for capability questions ('who has built X') — roster_semantic_search ranks by " +
         "meaning; do NOT use it to rank people against a job description or its must-haves — " +
         "roster_shortlist_search returns coverage and per-requirement evidence; do NOT use it to " +
         "sweep career narratives in bulk — roster_digest_list pages compact digests; do NOT use " +
         "it for one person — expert_get returns their child records, cv_get the assembled CV. " +
         "Input: none; e.g. {}. Rows carry NO skills, languages, qualifications, experiences, " +
         "availability history or CV prose, and draft experts are excluded."),
     Authorize(Policy = McpScopes.Read)]
    public static async Task<IReadOnlyList<ExpertSummaryDto>> List(
        IExpertService experts, CancellationToken ct)
        => await experts.ListAsync(includeDrafts: false, ct);

    [McpServerTool(Name = "expert_get", ReadOnly = true, Destructive = false),
     Description(
         "Get ONE expert by id with every child record: languages, the full availability step " +
         "function (effectiveFrom + capacityPercent entries), skills with level and years, " +
         "qualifications and experiences — each child carrying its own id for follow-up writes. " +
         "Use it when you already have the expertId and need that person's exact structured " +
         "facts (a skill's years, an availability date, contact fields, draft status). Do NOT use " +
         "it to FIND people — roster_semantic_search answers capability questions, " +
         "roster_shortlist_search ranks against a job description; do NOT use it when the CV is " +
         "what's wanted (prose to review, render or quote verbatim, achievement-bullet ids) — " +
         "cv_get assembles the CV; do NOT loop it over the roster — expert_list for summary " +
         "rows, roster_digest_list for narrative digests; and do NOT use it when the request is " +
         "to CHANGE something — it only reads: expert_update edits root fields like title or " +
         "email, availability_add records a capacity change, and the child *_add / *_update tools " +
         "edit skills, languages, qualifications and experiences. Input: id — the expert GUID; " +
         "e.g. " +
         "{\"id\": \"7b2e8d3a-1111-2222-3333-444455556666\"}. An unknown id returns a " +
         "not_found error, never an empty expert. Returns stored data only — no CV layout, no " +
         "PDF, no relevance scores."),
     Authorize(Policy = McpScopes.Read)]
    public static Task<object> Get(
        IExpertService experts,
        [Description("Expert id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experts.GetAsync(id, ct));

    [McpServerTool(Name = "expert_create", ReadOnly = false, Destructive = false),
     Description(
         "Create ONE ACTIVE expert from root fields only — first/last name, title, email, and " +
         "optional phone, location, summary, photoUrl. The new expert is immediately visible to " +
         "the roster, search and staffing. Use it when a human is adding a known colleague. Do " +
         "NOT use it for a resume an agent just parsed — expert_create_draft stages a hidden " +
         "draft for human promotion, which is the ingestion path; do NOT use it to change an " +
         "existing person — expert_update; do NOT try to pass skills, languages, availability, " +
         "qualifications or experiences here — they are separate tools (expert_skill_add, " +
         "language_add, availability_add, qualification_add, experience_add) run after this one " +
         "returns the new id. Input: dto with the root fields; e.g. {\"dto\": {\"firstName\": " +
         "\"Jane\", \"lastName\": \"Doe\", \"title\": \"Senior Engineer\", \"email\": " +
         "\"jane@example.com\", \"location\": \"Berlin\"}}. The email must be unique among " +
         "ACTIVE experts — a clash returns a conflict error, malformed fields a validation error " +
         "with per-field detail. Returns the created expert (with its new id) and NO children."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Create(
        IExpertService experts,
        [Description("Root fields of the new expert. Required: firstName, lastName, title, " +
                     "email (unique). Optional: phone, location, summary, photoUrl.")]
        SaveExpertDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experts.CreateAsync(dto, ct));

    [McpServerTool(Name = "expert_create_draft", ReadOnly = false, Destructive = false),
     Description(
         "Stage ONE DRAFT expert from root fields — the resume-ingestion path. Drafts are hidden " +
         "from the roster, search, staffing and every list until a HUMAN promotes them, so this is " +
         "the tool for anything extracted from a pasted CV or resume rather than confirmed by a " +
         "person. Unlike expert_create it accepts an EMPTY email, because a resume often has " +
         "none — never invent one. Do NOT use it when a human is entering a colleague they know " +
         "— expert_create makes an active expert; do NOT use it to amend an existing draft — " +
         "expert_update by id. Input: dto with whatever the source text actually supports; e.g. " +
         "{\"dto\": {\"firstName\": \"Jane\", \"lastName\": \"Doe\", \"title\": " +
         "\"Backend Engineer\", \"email\": \"\"}}. Returns the draft (with its new id) plus a " +
         "duplicateWarning when a same-name expert already exists — surface that warning, do not " +
         "silently merge. Children are NOT created here; add them by id afterwards."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> CreateDraft(
        IExpertService experts,
        [Description("Root fields extracted from the source text. email may be an empty string " +
                     "when the resume has none — never fabricate one.")]
        SaveExpertDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experts.CreateDraftAsync(dto, ct));

    [McpServerTool(Name = "expert_update", ReadOnly = false, Destructive = false),
     Description(
         "CHANGE an existing expert's root fields by id — name, title, email, phone, location, " +
         "summary, photoUrl. Use it for edits like 'change their title to Staff Engineer', 'fix " +
         "their email', 'update their professional summary'. PARTIAL UPDATE: send only the " +
         "field(s) you are changing — every field you omit (or send as null) keeps its current " +
         "value, so a single-field edit like a title change is one call with no prior read needed. " +
         "To CLEAR an optional field (phone, location, summary, photoUrl) to empty, send it as an " +
         "empty string, not null — null means 'leave unchanged'. Do NOT use expert_get for a " +
         "change — that only reads; do NOT use it for children: skills go through " +
         "expert_skill_add/update, capacity through availability_add (capacity is a dated step " +
         "function, not a root field), and likewise language_*, qualification_*, experience_*; do " +
         "NOT use it to create anyone — expert_create / expert_create_draft. Input: id " +
         "(expert GUID) + dto; e.g. {\"id\": \"7b2e8d3a-1111-2222-3333-444455556666\", \"dto\": " +
         "{\"title\": \"Staff Engineer\"}}. An unknown id returns not_found; a supplied firstName " +
         "or lastName cannot be blank. Returns the updated expert, children untouched; it never " +
         "promotes a draft."),
     Authorize(Policy = McpScopes.Write)]
    public static Task<object> Update(
        IExpertService experts,
        [Description("Expert id (GUID).")] Guid id,
        [Description("Only the root field(s) to change — omitted or null fields keep their " +
                     "current value. Send an empty string to clear an optional field.")]
        UpdateExpertDto dto,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experts.PatchAsync(id, dto, ct));

    [McpServerTool(Name = "expert_delete", ReadOnly = false, Destructive = true, Idempotent = true),
     Description(
         "DESTRUCTIVE, irreversible: delete one expert by id together with every child — " +
         "languages, availability, skills, qualifications, experiences and their achievement " +
         "bullets — and their search-index chunks. Use it only on an explicit human instruction to " +
         "remove a person's record. Do NOT use it to take someone off staffing consideration " +
         "(availability_add with capacityPercent 0 expresses that), to drop one child record (the " +
         "child's own *_delete tool does), or to discard a bad draft you would rather correct " +
         "(expert_update). Input: id (expert GUID); e.g. {\"id\": " +
         "\"7b2e8d3a-1111-2222-3333-444455556666\"}. Requires the admin scope; idempotent — " +
         "deleting an already-deleted id is not an error. Returns no expert data."),
     Authorize(Policy = McpScopes.Admin)]
    public static Task<object> Delete(
        IExpertService experts,
        [Description("Expert id (GUID).")] Guid id,
        CancellationToken ct)
        => McpToolExecutor.RunAsync(() => experts.DeleteAsync(id, ct));
}
