using System.Text;
using System.Text.Json;
using CvManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

/// <summary>The typed shortlist request: the job description plus the optional retrieval filters
/// that are passed through verbatim to the <c>roster_shortlist_search</c> tool.</summary>
public sealed record ShortlistAgentRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null,
    int? TopK = null);

/// <summary>Per-requirement evidence as reported by the shortlist tool.</summary>
public sealed record ShortlistToolEvidence(
    string Requirement, bool Matched, string? Snippet = null, double? Similarity = null);

/// <summary>One candidate as reported by the shortlist tool (coverage-ranked, with evidence).</summary>
public sealed record ShortlistToolCandidate(
    Guid EmployeeId,
    string Name,
    string Title,
    double Score,
    int MatchedCount,
    int TotalRequirements,
    IReadOnlyList<ShortlistToolEvidence> Evidence);

/// <summary>The shortlist tool's result as captured on the agent side. <see cref="Error"/> is the
/// tool's soft-error field (e.g. embedding backend down).</summary>
public sealed record ShortlistToolPayload(IReadOnlyList<ShortlistToolCandidate> Results, string? Error = null);

/// <summary>
/// What one shortlist run produced: the model reply (text + token usage, for metering), the
/// requirement strings the model actually passed to the tool, and the captured tool result.
/// <see cref="Tool"/> is null when the model never invoked the tool (or its result was unreadable);
/// the endpoint treats that as an upstream fault.
/// </summary>
public sealed record ShortlistAgentOutcome(
    AgentReply Reply,
    IReadOnlyList<string> Requirements,
    ShortlistToolPayload? Tool);

/// <summary>
/// JD-driven shortlisting agent. A Microsoft Agent Framework <see cref="ChatClientAgent"/> backed
/// by the configured chat model, narrowed to the single <c>roster_shortlist_search</c> MCP tool.
/// One run is a single session: the model reads the job description, distills 3-8 requirement
/// strings, calls the tool once (with any caller-supplied filters passed through verbatim), and
/// then returns only minimal rationale JSON. The deterministic candidate data never flows through
/// model text — a per-run decorating <see cref="AIFunction"/> captures the tool call's arguments
/// and result so the endpoint composes the response from tool-sourced facts.
/// </summary>
public sealed class ShortlistAgent
{
    /// <summary>The one MCP tool this agent uses.</summary>
    private const string ShortlistToolName = "roster_shortlist_search";

    private const string Instructions =
        """
        You are the Shortlist assistant for a CV Manager. The user message contains a job
        description (and possibly a JSON object of extra tool arguments). Your job:

        1. Read the job description and distill it into 3-8 short capability requirements, one
           phrase each (e.g. "event streaming with Kafka", "led a platform team").
        2. Call the roster_shortlist_search tool exactly once, passing those phrases as the
           "requirements" argument. If the user message includes a JSON object labelled as extra
           tool arguments, pass each of its fields to the tool verbatim, unchanged.
        3. After the tool returns, reply with ONLY a JSON array — no prose, no markdown fences,
           no explanations — of this exact shape:
           [{"employeeId":"<id>","rationale":"<one or two sentences>"}]
           Include one entry per candidate the tool returned, using exactly the employeeId values
           from the tool result. Each rationale must be grounded strictly in that candidate's
           per-requirement evidence from the tool result — never invent skills, experience, or
           facts the evidence does not contain.

        If the tool returns an error or no candidates, reply with exactly [] (an empty JSON array).
        You have read-only access and cannot change any data.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<AITool>? _tools;

    public ShortlistAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "shortlist";

    public async Task<ShortlistAgentOutcome> ShortlistAsync(ShortlistAgentRequest request, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(ct);

        // Per-run capture seam: decorate the shortlist AIFunction so this run records the
        // arguments the model sent and the result the tool returned. The ChatClientAgent itself
        // is rebuilt per run (it is cheap — the expensive part, the MCP tool listing, is cached),
        // which keeps the capture strictly request-scoped.
        var capture = new ShortlistToolCapture();
        var runTools = tools
            .Select(t => t is AIFunction f && f.Name == ShortlistToolName
                ? new CapturingShortlistFunction(f, capture)
                : t)
            .ToList();

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: Instructions,
            name: "Shortlist",
            description: "Shortlists roster candidates against a job description (read-only, advisory).",
            tools: runTools,
            loggerFactory: _loggerFactory);

        var session = await agent.CreateSessionAsync(ct);
        using var metering = Usage.MeteringScope.Begin();
        var response = await agent.RunAsync(BuildPrompt(request), session, null, ct);
        var usage = response.Usage;
        var (modelId, latencyMs) = metering.Snapshot();
        var reply = new AgentReply(
            response.Text,
            usage?.InputTokenCount ?? 0,
            usage?.OutputTokenCount ?? 0,
            usage?.TotalTokenCount ?? 0,
            modelId,
            latencyMs);

        return new ShortlistAgentOutcome(reply, capture.Requirements ?? [], capture.Payload);
    }

    private static string BuildPrompt(ShortlistAgentRequest request)
    {
        var filters = new Dictionary<string, object?>();
        if (request.AvailableOn is { } availableOn)
        {
            filters["availableOn"] = availableOn.ToString("yyyy-MM-dd");
        }

        if (request.SkillIds is { Length: > 0 } skillIds)
        {
            filters["skillIds"] = skillIds;
        }

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            filters["location"] = request.Location;
        }

        if (request.MinYears is { } minYears)
        {
            filters["minYears"] = minYears;
        }

        if (request.TopK is { } topK)
        {
            filters["topK"] = topK;
        }

        var prompt = new StringBuilder();
        if (filters.Count > 0)
        {
            prompt.AppendLine("Extra tool arguments (pass each field to roster_shortlist_search verbatim):");
            prompt.AppendLine(JsonSerializer.Serialize(filters, Json));
            prompt.AppendLine();
        }

        prompt.AppendLine("Job description:");
        prompt.Append(request.JobDescription);
        return prompt.ToString();
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

            // Narrow to roster_shortlist_search only: shortlisting needs nothing else, and a
            // tighter tool surface keeps the model on task. mcp:read already hides write tools.
            var tools = await _toolSource.GetToolsAsync(ct);
            _tools = tools.Where(t => t.Name == ShortlistToolName).ToList();
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Mutable per-run capture cell filled in by <see cref="CapturingShortlistFunction"/>.</summary>
internal sealed class ShortlistToolCapture
{
    public IReadOnlyList<string>? Requirements { get; set; }
    public ShortlistToolPayload? Payload { get; set; }
}

/// <summary>
/// Decorates the shortlist <see cref="AIFunction"/> to record, per run, the requirement strings
/// the model passed and the tool's parsed result — the endpoint composes its response from these
/// tool-sourced facts, never from model prose. The wrapped tool behaves identically otherwise.
/// </summary>
internal sealed class CapturingShortlistFunction(AIFunction inner, ShortlistToolCapture capture)
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
        capture.Requirements = ExtractRequirements(arguments) ?? capture.Requirements;
        capture.Payload = ExtractPayload(result) ?? capture.Payload;
        return result;
    }

    private static IReadOnlyList<string>? ExtractRequirements(AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue("requirements", out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string[] strings => strings,
            IEnumerable<string> strings => strings.ToList(),
            JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToList(),
            _ => null,
        };
    }

    /// <summary>Leniently digs the shortlist payload out of whatever shape the tool result
    /// arrives in — see <see cref="ToolResultPayload"/> (shared with the tailoring agent).</summary>
    private static ShortlistToolPayload? ExtractPayload(object? result)
        => ToolResultPayload.Extract<ShortlistToolPayload>(
            result, obj => obj.ContainsKey("results") || obj.ContainsKey("Results"), Json);
}
