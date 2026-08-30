using System.Text;
using System.Text.Json;
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

/// <summary>The shortlist tool's result. <see cref="Error"/> is the tool's soft-error field
/// (e.g. embedding backend down).</summary>
public sealed record ShortlistToolPayload(IReadOnlyList<ShortlistToolCandidate> Results, string? Error = null);

/// <summary>What one shortlist run produced: the rationale-model reply (text + token usage, for
/// metering), the requirement strings the retrieval was invoked with, and the tool result.
/// Since P1T-117 the requirements come from the JD extractor and the tool is invoked
/// deterministically — <see cref="Tool"/> is null only when retrieval itself failed.</summary>
public sealed record ShortlistAgentOutcome(
    AgentReply Reply,
    IReadOnlyList<string> Requirements,
    ShortlistToolPayload? Tool);

/// <summary>The structured rationale reply (P1T-117): schema-constrained on the wire; the
/// composer still guards ids and blanks — schema validity is not semantic validity.</summary>
public sealed record ShortlistRationalePayload(IReadOnlyList<ShortlistRationaleEntry?>? Rationales);

public sealed record ShortlistRationaleEntry(string? EmployeeId, string? Rationale);

/// <summary>
/// The shortlist's rationale generator. Since P1T-117 this is a tool-less structured call: the
/// requirements come from <see cref="JdRequirementExtractor"/>, the retrieval runs
/// deterministically (<see cref="IShortlistSearch"/>), and this class only turns the captured
/// evidence into one grounded rationale per candidate. No agent identity, no MCP access — all
/// facts arrive pre-assembled in the prompt; the composer's corruption guard keeps model text
/// out of the deterministic fields.
/// </summary>
public sealed class ShortlistAgent
{
    private const string Instructions =
        """
        You write per-candidate rationales for a shortlist against a job description. You are
        given the job description and, for each candidate, their per-requirement evidence from
        the retrieval tool. Reply with the structured object: one rationale (one or two
        sentences) per candidate, using exactly the employeeId values given. Each rationale must
        be grounded strictly in that candidate's per-requirement evidence — never invent skills,
        experience, or facts the evidence does not contain.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IChatClient _chatClient;

    public ShortlistAgent(IChatClient chatClient) => _chatClient = chatClient;

    public string Name => "shortlist";

    /// <summary>One grounded rationale per candidate in <paramref name="payload"/>.</summary>
    public async Task<AgentReply> RationalesAsync(
        string jobDescription, ShortlistToolPayload payload, CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Job description:");
        prompt.AppendLine(jobDescription);
        prompt.AppendLine();
        prompt.AppendLine("Candidates with per-requirement evidence:");
        prompt.AppendLine(JsonSerializer.Serialize(payload.Results, Json));

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                AIJsonUtilities.CreateJsonSchema(typeof(ShortlistRationalePayload)), "shortlist_rationales"),
        };

        using var metering = Usage.MeteringScope.Begin();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.System, Instructions), new ChatMessage(ChatRole.User, prompt.ToString())],
            options,
            ct);
        var run = metering.Snapshot();
        return new AgentReply(
            response.Text,
            response.Usage?.InputTokenCount ?? 0,
            response.Usage?.OutputTokenCount ?? 0,
            response.Usage?.TotalTokenCount ?? 0,
            run.ModelId ?? response.ModelId,
            run.LatencyMs > 0 ? run.LatencyMs : clock.ElapsedMilliseconds,
            run.Iterations,
            run.ToolSequence);
    }
}
