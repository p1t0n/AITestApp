using System.Text.Json;
using CvManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

/// <summary>One write-tool invocation as observed on the agent side: which tool, whether the MCP
/// layer reported success, and the structured error text when it did not.</summary>
public sealed record IngestionToolCall(string Tool, bool Succeeded, string? Error);

/// <summary>
/// What one ingestion run produced: the model reply (metering), the created draft's id and
/// duplicate warning captured from the <c>employee_create_draft</c> result, every write-tool
/// invocation in order (the run service composes counts and degradation notes from these — never
/// from model text), and the model's minimal closing JSON (skill proposals, abort flag).
/// </summary>
public sealed record ResumeIngestionOutcome(
    AgentReply Reply,
    Guid? EmployeeId,
    string? DuplicateWarning,
    IReadOnlyList<IngestionToolCall> ToolCalls,
    string ClosingJson);

/// <summary>
/// The first <c>mcp:write</c> agent (P1T-92): pasted resume text → structured extraction → real
/// chained MCP write calls staging a DRAFT employee with children. The MCP layer returns
/// structured validation errors, which flow back to the model as tool results so it can
/// self-correct (bounded by instruction to ~2 retries per item). Deterministic facts — the draft
/// id, duplicate warning, per-tool success/failure — are captured from tool results by per-run
/// decorators, never parsed out of model prose. Honesty rules carried from the P1T-81 gate:
/// nothing that is not in the resume text, no invented emails or date precision, and Availability
/// is never touched.
/// </summary>
public sealed class ResumeIngestionAgent
{
    private const string CreateDraftTool = "employee_create_draft";

    /// <summary>The narrowed tool surface: the draft create, the four child adds, and the catalog
    /// listing the model needs for skill matching. Nothing else — notably not skill_create (the
    /// agent proposes catalog additions, humans approve them) and no Availability tools.</summary>
    private static readonly string[] ToolNames =
    [
        CreateDraftTool,
        "language_add",
        "employee_skill_add",
        "qualification_add",
        "experience_add",
        "skill_list",
    ];

    private const string Instructions =
        """
        You are the Resume Ingestion assistant for a CV Manager. The user message is the raw text
        of one resume. Stage it as a DRAFT employee by calling tools, then report.

        Extraction rules — honesty above completeness:
        - Use ONLY facts present in the resume text. If a value is absent (even email), leave it
          empty — NEVER invent one.
        - Dates: "yyyy-MM-dd"; when the text is less precise use the first day of the stated
          month or year. Never invent precision beyond that. A current role has a null endDate.
        - Never touch availability/capacity — resumes do not state it.

        Procedure:
        1. Call skill_list once to load the skill catalog.
        2. Call employee_create_draft with the person's root fields (firstName, lastName, title,
           email — empty string if the resume has none — phone, location, summary).
        3. Using the returned employee id, add children:
           - language_add for each spoken language (level: Basic|Conversational|Professional|Fluent|Native).
           - employee_skill_add for each skill that matches a catalog entry — match by meaning,
             not just exact spelling (e.g. "ReactJS" matches "React"). Use the catalog skillId.
             Level (Beginner|Intermediate|Advanced|Expert) and yearsExperience only as evidenced.
           - qualification_add for each degree/certification (type: Degree|Certification).
           - experience_add for each role: company, title, dates, summary, achievements (verbatim-
             faithful result bullets), and skillIds for catalog skills evidenced in THAT role.
        4. If a tool returns a validation error, read its fields, fix the arguments, and retry —
           at most 2 retries for the same item, then skip it and move on.
        5. If employee_create_draft itself still fails after 2 retries, STOP — do not add children.
        6. Skills with no catalog match (even by meaning) are NOT added and NOT created — collect
           their names as proposals for human review.

        When done, reply with ONLY this JSON — no prose, no markdown fences:
        {"proposals":["<unmatched skill name>", ...],"aborted":false,"abortReason":null}
        Set "aborted" to true (with a short reason) only when step 5 stopped the run.
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<AITool>? _tools;

    public ResumeIngestionAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "resume-ingestion";

    public async Task<ResumeIngestionOutcome> IngestAsync(string resumeText, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(ct);

        // Per-run capture seam (same pattern as the shortlist agent): every write tool is wrapped
        // so this run records the chain of MCP results the response is composed from.
        var capture = new IngestionCapture();
        var runTools = tools
            .Select(t => t is AIFunction f && f.Name != "skill_list"
                ? new CapturingIngestionFunction(f, capture)
                : t)
            .ToList();

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: Instructions,
            name: "ResumeIngestion",
            description: "Stages a resume as a draft employee via MCP write tools (draft-then-promote).",
            tools: runTools,
            loggerFactory: _loggerFactory);

        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync("Resume text:\n\n" + resumeText, session, null, ct);
        var usage = response.Usage;
        var reply = new AgentReply(
            response.Text,
            usage?.InputTokenCount ?? 0,
            usage?.OutputTokenCount ?? 0,
            usage?.TotalTokenCount ?? 0);

        return new ResumeIngestionOutcome(
            reply, capture.EmployeeId, capture.DuplicateWarning, capture.Calls, response.Text);
    }

    private async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct)
    {
        if (_tools is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is { } stillCached)
            {
                return stillCached;
            }

            var tools = await _toolSource.GetToolsAsync(ct);
            _tools = tools.Where(t => ToolNames.Contains(t.Name)).ToList();
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Mutable per-run capture filled by <see cref="CapturingIngestionFunction"/>.</summary>
internal sealed class IngestionCapture
{
    public Guid? EmployeeId { get; set; }
    public string? DuplicateWarning { get; set; }
    public List<IngestionToolCall> Calls { get; } = [];
}

/// <summary>The MCP structured tool error, as mapped by the server's McpToolErrorMapper.</summary>
internal sealed record IngestionErrorPayload(string Code, string Message);

/// <summary>The employee_create_draft result: the created draft plus the duplicate warning.</summary>
internal sealed record IngestionDraftPayload(IngestionDraftEmployee Employee, string? DuplicateWarning);

internal sealed record IngestionDraftEmployee(Guid Id);

/// <summary>
/// Decorates each write <see cref="AIFunction"/> to record, per run, whether the MCP layer
/// accepted the call — a result carrying the structured error shape ({"code","message",...}) is a
/// failure — plus the created draft's id and duplicate warning from <c>employee_create_draft</c>.
/// The wrapped tool behaves identically otherwise, so the model still sees the error and retries.
/// </summary>
internal sealed class CapturingIngestionFunction(AIFunction inner, IngestionCapture capture)
    : DelegatingAIFunction(inner)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        Record(result);
        return result;
    }

    private void Record(object? result)
    {
        var error = ToolResultPayload.Extract<IngestionErrorPayload>(
            result, obj => obj.ContainsKey("code") && obj.ContainsKey("message"), Json);

        capture.Calls.Add(new IngestionToolCall(
            Name, error is null, error is null ? null : $"{error.Code}: {error.Message}"));

        if (error is null && Name == "employee_create_draft")
        {
            var draft = ToolResultPayload.Extract<IngestionDraftPayload>(
                result, obj => obj.ContainsKey("employee") || obj.ContainsKey("Employee"), Json);
            if (draft is { Employee.Id: var id } && id != Guid.Empty)
            {
                capture.EmployeeId = id;
            }

            capture.DuplicateWarning ??= draft?.DuplicateWarning;
        }
    }
}
