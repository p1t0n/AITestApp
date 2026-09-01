using System.Text.Json;
using System.Text.Json.Nodes;
using ExpertToJob.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>One anonymized strong-phrasing exemplar as captured from the exemplar tool.</summary>
public sealed record TailoringExemplar(string Text, double Similarity);

/// <summary>The exemplars the tool returned for one selected achievement bullet.</summary>
public sealed record TailoringBulletExemplars(Guid AchievementId, IReadOnlyList<TailoringExemplar> Exemplars);

/// <summary>The exemplar tool's result as captured on the agent side. <see cref="Error"/> is the
/// tool's soft-error field (e.g. embedding backend down); callers degrade rather than fail.</summary>
public sealed record TailoringExemplarPayload(IReadOnlyList<TailoringBulletExemplars> Results, string? Error = null);

/// <summary>One achievement bullet as captured from the cv_get result.</summary>
public sealed record TailoringCvAchievement(Guid Id, string Text);

/// <summary>One experience as captured from the cv_get result — enough context to resolve a
/// rewrite's original text/experience id and to feed the fabrication guard (dates and durations
/// in the experience header legitimise numeric tokens in a rewrite).</summary>
public sealed record TailoringCvExperience(
    Guid Id,
    string Company,
    string Title,
    string Period,
    string? Summary,
    IReadOnlyList<TailoringCvAchievement> Achievements);

/// <summary>The slice of the cv_get result the rewrite flow needs, captured on the agent side.</summary>
public sealed record TailoringCvPayload(IReadOnlyList<TailoringCvExperience> Experiences);

/// <summary>
/// What one tailoring run produced: the model reply (text = turn 1's tailoring markdown, usage
/// summed over both turns, for metering), turn 2's raw rewrites JSON text (empty when the rewrite
/// turn failed), and the captured tool traffic — the achievement ids the model selected, the
/// exemplar tool's result, and the cv_get result the endpoint resolves originals from.
/// </summary>
public sealed record TailoringAgentOutcome(
    AgentReply Reply,
    string RewritesText,
    IReadOnlyList<Guid> SelectedAchievementIds,
    TailoringExemplarPayload? Exemplars,
    TailoringCvPayload? Cv);

/// <summary>
/// Read-only agent that tailors one employee's CV to a target job description. A Microsoft Agent
/// Framework <see cref="ChatClientAgent"/> backed by the configured chat model. The <c>cv_get</c>
/// call is a fixed prerequisite, so it runs deterministically in code (P1T-131) and its verbatim
/// result opens the session; the model's tool surface is exactly <c>style_exemplar_search</c> —
/// the genuinely dynamic call, where the model picks which bullets deserve exemplars. One run is
/// a single 2-turn session: turn 1 selects up to 8 JD-relevant achievement ids, calls the
/// exemplar tool once, and answers with the advisory prose exactly as before; turn 2 (driven by
/// this class, not the caller) returns only minimal rewrites JSON. The captured cv_get result and
/// the decorated exemplar call let the endpoint compose rewrites from tool-sourced facts, never
/// from model text. The agent never fabricates data and writes nothing.
/// </summary>
public sealed class CvTailoringAgent
{
    /// <summary>The CV tool; <c>cv_get</c> already bundles the full CV including achievement ids.</summary>
    private const string CvTool = "cv_get";

    /// <summary>The style exemplar tool the model calls once with its selected achievement ids.</summary>
    private const string ExemplarTool = "style_exemplar_search";

    /// <summary>The fixed user message that drives turn 2. Keeping it agent-side (instead of a
    /// delimited single response) leaves turn 1's text byte-identical to the pre-rewrite answer
    /// and gives the rewrite turn its own failure isolation.</summary>
    private const string RewriteTurnPrompt = "Now the rewrites.";

    private const string Instructions =
        """
        You are the CV Tailoring assistant for ExpertToJob. You are given a target job
        description and the employee's full CV — the verbatim result of the cv_get tool, already
        fetched for you and included in the message. The conversation has exactly two steps.

        STEP 1 — the current message. From the CV's experiences, select up to 8 achievement ids
        whose bullets are most relevant to the job description, and call the
        style_exemplar_search tool exactly once, passing those ids as the "achievementIds"
        argument. Then produce:

        1. A ready-to-paste rewritten professional summary (a short paragraph) aimed at the role.
        2. Concrete tailoring guidance: which skills and experiences to emphasise, which to drop or
           de-emphasise, and how to reorder them for this job description.

        Do not mention the exemplars or the upcoming rewrites in this reply. Use ONLY facts from
        the provided CV — never invent skills, experience, qualifications, or achievements the CV
        does not contain. If the CV result reports the employee was not found or contains an
        error, say so plainly and stop (do not call style_exemplar_search). You have read-only
        access and cannot change any data.

        STEP 2 — the user will then say "Now the rewrites." Reply with ONLY a JSON array — no
        prose, no markdown fences, no explanations — of this exact shape:
        [{"achievementId":"<id>","rewritten":"<the rewritten bullet>"}]
        Rewrite each achievement bullet you selected in step 1, tailored to the job description.
        The style_exemplar_search result contains STYLE EXEMPLARS — bullets from OTHER people's
        CVs, shown ONLY as examples of strong phrasing. The candidate did NOT do these things.
        Imitate the writing QUALITY; NEVER borrow their facts, numbers, systems, or achievements.
        Every claim in your output must be traceable to THIS candidate's CV. If the exemplar tool
        returned an error or no exemplars, still rewrite the selected bullets in the same spirit.
        If no achievement bullets were selected, reply with exactly [] (an empty JSON array).
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CvTailoringAgent> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<AITool>? _tools;

    public CvTailoringAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CvTailoringAgent>();
    }

    public string Name => "cv-tailoring";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<TailoringAgentOutcome> TailorAsync(
        Guid employeeId, string jobDescription, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(ct);

        // Fixed order → code (P1T-131): cv_get is a hard prerequisite of the whole run — the
        // employee is known before the model says a word — so it is invoked deterministically
        // here (the P1T-117 shortlist-retrieval pattern) instead of hoping the tool loop calls
        // it. The exemplar call stays model-driven: genuinely dynamic, the model picks which
        // bullets deserve exemplars.
        var cvGet = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == CvTool)
            ?? throw new HttpRequestException($"The MCP server did not expose the {CvTool} tool.");
        var cvResult = await cvGet.InvokeAsync(
            new AIFunctionArguments { ["employeeId"] = employeeId }, ct);

        // Per-run capture seam (mirrors the shortlist agent): the cv_get result is captured from
        // the deterministic call above; the exemplar decorator records the model's selection and
        // the tool's payload. The ChatClientAgent itself is rebuilt per run (cheap — the
        // expensive MCP tool listing is cached), which keeps the capture strictly request-scoped.
        var capture = new TailoringCapture
        {
            Cv = ToolResultPayload.Extract<TailoringCvPayload>(
                cvResult,
                obj => obj.ContainsKey("experiences") || obj.ContainsKey("Experiences"),
                Json),
        };
        var runTools = tools
            .Where(t => t.Name == ExemplarTool)
            .Select(t => t is AIFunction f ? (AITool)new CapturingExemplarFunction(f, capture) : t)
            .ToList();

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: Instructions,
            name: "CvTailoring",
            description: "Tailors an employee's CV to a target job description (read-only, advisory).",
            tools: runTools,
            loggerFactory: _loggerFactory);

        var session = await agent.CreateSessionAsync(ct);
        using var metering = Usage.MeteringScope.Begin();

        // The opening message carries the JD and the verbatim cv_get result — the model reads
        // the CV instead of fetching it.
        var question =
            $"""
             Tailor the CV of employee {employeeId} to this job description:

             {jobDescription}

             The employee's full CV (the verbatim cv_get result):
             {RenderToolResult(cvResult)}
             """;

        // Turn 1: the tailoring markdown — this is the answer, byte-identical in behavior to the
        // pre-rewrite agent. A failure here is an upstream fault and propagates to the endpoint.
        var first = await agent.RunAsync(question, session, null, ct);

        // Turn 2: the rewrites JSON. A model-side failure here must not cost the caller the
        // answer it already has, so it degrades to "no rewrites" instead of propagating.
        var rewritesText = string.Empty;
        UsageDetails? secondUsage = null;
        try
        {
            var second = await agent.RunAsync(RewriteTurnPrompt, session, null, ct);
            rewritesText = second.Text;
            secondUsage = second.Usage;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CV tailoring rewrite turn failed; returning the answer without rewrites.");
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

        return new TailoringAgentOutcome(
            reply, rewritesText, capture.SelectedAchievementIds ?? [], capture.Exemplars, capture.Cv);
    }

    /// <summary>The cv_get result as prompt text: tool results usually arrive as JSON text (or a
    /// TextContent wrapping it); anything else serializes as JSON.</summary>
    private static string RenderToolResult(object? result) => result switch
    {
        null => "(the tool returned no result)",
        string text => text,
        Microsoft.Extensions.AI.TextContent content => content.Text,
        _ => JsonSerializer.Serialize(result, Json),
    };

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

            // Narrow to cv_get + style_exemplar_search: tailoring needs nothing else, and a
            // tighter tool surface keeps the model on task. mcp:read already hides write tools.
            var tools = await _toolSource.GetToolsAsync(ct);
            _tools = tools.Where(t => t.Name is CvTool or ExemplarTool).ToList();
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Mutable per-run capture cell filled in by the capturing tool wrappers.</summary>
internal sealed class TailoringCapture
{
    public TailoringCvPayload? Cv { get; set; }
    public IReadOnlyList<Guid>? SelectedAchievementIds { get; set; }
    public TailoringExemplarPayload? Exemplars { get; set; }
}

/// <summary>Decorates <c>style_exemplar_search</c> to record, per run, the achievement ids the
/// model selected and the tool's parsed exemplar result — the endpoint's fabrication guard checks
/// rewrites against exactly the exemplars shown this run. Behaves identically otherwise.</summary>
internal sealed class CapturingExemplarFunction(AIFunction inner, TailoringCapture capture)
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
        capture.SelectedAchievementIds = ExtractAchievementIds(arguments) ?? capture.SelectedAchievementIds;
        capture.Exemplars = ToolResultPayload.Extract<TailoringExemplarPayload>(
            result,
            obj => obj.ContainsKey("results") || obj.ContainsKey("Results"),
            Json) ?? capture.Exemplars;
        return result;
    }

    /// <summary>Reads the "achievementIds" argument leniently: Guid/string collections or the
    /// JsonElement array the model's raw function call arrives as.</summary>
    private static IReadOnlyList<Guid>? ExtractAchievementIds(AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue("achievementIds", out var raw) || raw is null)
        {
            return null;
        }

        if (raw is IEnumerable<Guid> guids)
        {
            return guids.ToList();
        }

        try
        {
            if (JsonSerializer.SerializeToNode(raw, Json) is not JsonArray array)
            {
                return null;
            }

            var ids = new List<Guid>();
            foreach (var item in array)
            {
                if (item is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && Guid.TryParse(text, out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
