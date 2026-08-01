using System.Text.Json;
using System.Text.Json.Nodes;
using CvManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

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
/// Framework <see cref="ChatClientAgent"/> backed by the configured chat model, narrowed to the
/// <c>cv_get</c> + <c>style_exemplar_search</c> MCP tools. One run is a single 2-turn session:
/// turn 1 fetches the CV, selects up to 8 JD-relevant achievement ids, calls the exemplar tool
/// once, and answers with the advisory prose exactly as before; turn 2 (driven by this class, not
/// the caller) returns only minimal rewrites JSON. Per-run decorating <see cref="AIFunction"/>s
/// capture the cv_get result and the exemplar call so the endpoint composes rewrites from
/// tool-sourced facts, never from model text. The agent never fabricates data and writes nothing.
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
        You are the CV Tailoring assistant for a CV Manager. You are given an employee id and a
        target job description. The conversation has exactly two steps.

        STEP 1 — the current message. Call the cv_get tool to fetch that employee's full CV. Then
        select up to 8 achievement ids from the CV's experiences whose bullets are most relevant
        to the job description, and call the style_exemplar_search tool exactly once, passing
        those ids as the "achievementIds" argument. Then produce:

        1. A ready-to-paste rewritten professional summary (a short paragraph) aimed at the role.
        2. Concrete tailoring guidance: which skills and experiences to emphasise, which to drop or
           de-emphasise, and how to reorder them for this job description.

        Do not mention the exemplars or the upcoming rewrites in this reply. Use ONLY facts
        returned by cv_get — never invent skills, experience, qualifications, or achievements the
        CV does not contain. If cv_get reports the employee was not found, say so plainly and stop
        (do not call style_exemplar_search). You have read-only access and cannot change any data.

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

    public async Task<TailoringAgentOutcome> TailorAsync(string question, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(ct);

        // Per-run capture seam (mirrors the shortlist agent): decorate both AIFunctions so this
        // run records the cv_get result, the achievement ids the model selected, and the exemplar
        // result. The ChatClientAgent itself is rebuilt per run (cheap — the expensive MCP tool
        // listing is cached), which keeps the capture strictly request-scoped.
        var capture = new TailoringCapture();
        var runTools = tools
            .Select(t => t switch
            {
                AIFunction f when f.Name == CvTool => new CapturingCvGetFunction(f, capture),
                AIFunction f when f.Name == ExemplarTool => new CapturingExemplarFunction(f, capture),
                _ => t,
            })
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

        var (modelId, latencyMs) = metering.Snapshot();
        var reply = new AgentReply(
            first.Text,
            (first.Usage?.InputTokenCount ?? 0) + (secondUsage?.InputTokenCount ?? 0),
            (first.Usage?.OutputTokenCount ?? 0) + (secondUsage?.OutputTokenCount ?? 0),
            (first.Usage?.TotalTokenCount ?? 0) + (secondUsage?.TotalTokenCount ?? 0),
            modelId,
            latencyMs);

        return new TailoringAgentOutcome(
            reply, rewritesText, capture.SelectedAchievementIds ?? [], capture.Exemplars, capture.Cv);
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

/// <summary>Decorates <c>cv_get</c> to record the CV the model fetched — the endpoint resolves
/// each rewrite's original bullet text and experience id from it (the Agents service may not
/// query the employee DB directly; MCP is the boundary). Behaves identically otherwise.</summary>
internal sealed class CapturingCvGetFunction(AIFunction inner, TailoringCapture capture)
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
        capture.Cv = ToolResultPayload.Extract<TailoringCvPayload>(
            result,
            obj => obj.ContainsKey("experiences") || obj.ContainsKey("Experiences"),
            Json) ?? capture.Cv;
        return result;
    }
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
