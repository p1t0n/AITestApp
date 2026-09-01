using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>The slice of the cv_get result the interview kit needs: the professional summary plus
/// the experiences (reusing the tailoring experience shape) — the corpus evidence quotes are
/// validated against.</summary>
public sealed record InterviewCvPayload(
    string? Summary,
    IReadOnlyList<TailoringCvExperience> Experiences);

/// <summary>
/// What one interview-kit run produced: the model reply (text = turn 1's markdown kit, usage
/// summed over both turns, for metering), turn 2's raw questions JSON text (empty when that turn
/// failed), and the captured cv_get result the composer validates evidence quotes against.
/// </summary>
public sealed record InterviewKitOutcome(
    AgentReply Reply,
    string QuestionsText,
    InterviewCvPayload? Cv);

/// <summary>
/// Read-only agent that generates gap-targeted interview questions for one employee against a
/// target job description (P1T-102). A Microsoft Agent Framework <see cref="ChatClientAgent"/>
/// narrowed to the <c>cv_get</c> MCP tool. One run is a single 2-turn session: turn 1 fetches the
/// CV, analyses fit gaps, and answers with a readable interview kit; turn 2 (driven by this
/// class) returns only minimal questions JSON. A per-run decorating <see cref="AIFunction"/>
/// captures the cv_get result so the composer can vet every evidence quote against CV facts —
/// the model's evidence claims are checked, never trusted. The agent writes nothing.
/// </summary>
public sealed class InterviewKitAgent
{
    private const string CvTool = "cv_get";

    /// <summary>The fixed user message that drives turn 2 — same isolation pattern as the
    /// tailoring rewrites turn: the markdown kit stays intact when this turn fails.</summary>
    private const string QuestionsTurnPrompt = "Now the JSON.";

    private const string Instructions =
        """
        You are the Interview Kit assistant for ExpertToJob. You are given an employee id and a
        target job description. The conversation has exactly two steps.

        STEP 1 — the current message. Call the cv_get tool to fetch that employee's full CV. Then
        compare the CV against the job description and produce a readable interview kit in
        markdown:

        1. A short fit summary: where the CV clearly covers the role and where it does not.
        2. 5-8 targeted interview questions. Prioritise (a) requirements the CV does NOT evidence
           (gaps), and (b) CV claims relevant to the role that deserve probing (depth checks).
           For each question, name what it probes and, when it stems from a concrete CV item,
           quote that item.

        Use ONLY facts returned by cv_get — never invent skills, experience, or achievements the
        CV does not contain. If cv_get reports the employee was not found, say so plainly and
        stop. You have read-only access and cannot change any data.

        STEP 2 — the user will then say "Now the JSON." Reply with ONLY a JSON array — no prose,
        no markdown fences — of this exact shape:
        [{"question":"<the question>","probes":"<the gap or claim it probes>","evidence":"<an EXACT quote from the CV this stems from, or empty when it probes a gap the CV does not cover>"}]
        List the same questions as step 1. The evidence string, when present, must be copied
        verbatim from the cv_get result — an achievement bullet, an experience summary, or the
        professional summary. Never paraphrase inside evidence.
        """;

    private readonly IChatClient _chatClient;
    private readonly Mcp.IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<InterviewKitAgent> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<AITool>? _tools;

    public InterviewKitAgent(IChatClient chatClient, Mcp.IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<InterviewKitAgent>();
    }

    public string Name => "interview-kit";

    public async Task<InterviewKitOutcome> GenerateAsync(string question, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(ct);

        // Per-run capture seam (same as tailoring): decorate cv_get so this run records the CV
        // the model fetched; the composer validates evidence quotes against it.
        var capture = new InterviewKitCapture();
        var runTools = tools
            .Select(t => t is AIFunction f && f.Name == CvTool
                ? new InterviewCapturingCvFunction(f, capture)
                : t)
            .ToList();

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: Instructions,
            name: "InterviewKit",
            description: "Generates gap-targeted interview questions for an employee vs a job description (read-only).",
            tools: runTools,
            loggerFactory: _loggerFactory);

        var session = await agent.CreateSessionAsync(ct);
        using var metering = Usage.MeteringScope.Begin();

        // Turn 1: the markdown kit — a failure here is an upstream fault and propagates.
        var first = await agent.RunAsync(question, session, null, ct);

        // Turn 2: the questions JSON. A failure here must not cost the caller the kit it already
        // has, so it degrades to "no structured questions" instead of propagating.
        var questionsText = string.Empty;
        UsageDetails? secondUsage = null;
        try
        {
            var second = await agent.RunAsync(QuestionsTurnPrompt, session, null, ct);
            questionsText = second.Text;
            secondUsage = second.Usage;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Interview kit questions turn failed; returning the kit without structured questions.");
        }

        var run = metering.Snapshot();
        var reply = new AgentReply(
            first.Text,
            (first.Usage?.InputTokenCount ?? 0) + (secondUsage?.InputTokenCount ?? 0),
            (first.Usage?.OutputTokenCount ?? 0) + (secondUsage?.OutputTokenCount ?? 0),
            (first.Usage?.TotalTokenCount ?? 0) + (secondUsage?.TotalTokenCount ?? 0),
            run.ModelId,
            run.LatencyMs,
            run.Iterations,
            run.ToolSequence,
            run.Degradation);

        return new InterviewKitOutcome(reply, questionsText, capture.Cv);
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

            // Narrow to cv_get: the kit needs nothing else; a tighter tool surface keeps the
            // model on task. mcp:read already hides write tools.
            var tools = await _toolSource.GetToolsAsync(ct);
            _tools = tools.Where(t => t.Name is CvTool).ToList();
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Mutable per-run capture cell filled in by the capturing cv_get wrapper.</summary>
internal sealed class InterviewKitCapture
{
    public InterviewCvPayload? Cv { get; set; }
}

/// <summary>Decorates <c>cv_get</c> to record the CV the model fetched — evidence quotes are
/// validated against it (the Agents service may not query the employee DB directly; MCP is the
/// boundary). Behaves identically otherwise.</summary>
internal sealed class InterviewCapturingCvFunction(AIFunction inner, InterviewKitCapture capture)
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
        capture.Cv = ToolResultPayload.Extract<InterviewCvPayload>(
            result,
            obj => obj.ContainsKey("experiences") || obj.ContainsKey("Experiences"),
            Json) ?? capture.Cv;
        return result;
    }
}
