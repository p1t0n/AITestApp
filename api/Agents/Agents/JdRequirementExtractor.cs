using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>What one extraction produced. Exactly one of <see cref="Requirements"/> and
/// <see cref="FaultDetail"/> is non-null; <see cref="Reply"/> is always present — tokens were
/// spent either way, so the caller meters it (under <see cref="AgentName"/>) before deciding
/// what to do with a fault.</summary>
public sealed record JdExtractionOutcome(
    string AgentName,
    AgentReply Reply,
    JdRequirements? Requirements,
    string? FaultDetail);

/// <summary>The extraction seam: consumers (Shortlist, Match, Interview Kit, Roster Scan) take
/// this interface; tests substitute a fake.</summary>
public interface IJdRequirementExtractor
{
    /// <summary>Extracts the structured requirements from one job description.</summary>
    Task<JdExtractionOutcome> ExtractAsync(string jobDescription, CancellationToken ct = default);
}

/// <summary>
/// Tool-less structured extraction of a job description into <see cref="JdRequirements"/>
/// (P1T-116). Runs on the plain chat client — no agent identity, no MCP access — using native
/// json_schema output (<c>useJsonSchemaResponseFormat: true</c>, the method locked by the
/// P1T-115 probes). No cap-check, no metering here: those stay with the caller, per the
/// run-service convention. Transport exceptions propagate (the endpoint shell maps them);
/// an unparseable reply is reported as data via <see cref="JdExtractionOutcome.FaultDetail"/>.
/// </summary>
public sealed class JdRequirementExtractor : IJdRequirementExtractor
{
    public const string AgentName = "jd-extraction";

    private const string Instructions =
        """
        You extract hiring requirements from a job description. Reply with the structured object
        only — every fact must come from the JD text.

        Honesty rules, in priority order:
        1. Never invent a value. If the JD does not state seniority, use "Unspecified"; if it does
           not state minimum years, use null for minYears; if it names no location, use null.
        2. A requirement's priority is "MustHave" only when the JD marks it required/essential;
           "NiceToHave" when marked preferred/bonus; otherwise "Unspecified".
        3. For every requirement, put the exact JD phrase that states it in evidenceSpan — quote
           verbatim, do not paraphrase. If you cannot quote a phrase, set evidenceSpan to null and
           inferred to true.
        4. When the JD is unclear or contradictory about something, say so in ambiguities instead
           of guessing.

        Distill 3-8 requirements, one capability per entry, phrased short (e.g. "event streaming",
        "AWS infrastructure", "team leadership").
        """;

    private readonly IChatClient _chat;

    public JdRequirementExtractor(IChatClient chat) => _chat = chat;

    /// <summary>Extracts the structured requirements from one job description.</summary>
    public async Task<JdExtractionOutcome> ExtractAsync(
        string jobDescription, CancellationToken ct = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await _chat.GetResponseAsync<JdRequirements>(
            [
                new ChatMessage(ChatRole.System, Instructions),
                new ChatMessage(ChatRole.User, $"Job description:\n\n{jobDescription}"),
            ],
            useJsonSchemaResponseFormat: true,
            cancellationToken: ct);

        var reply = new AgentReply(
            response.Text,
            response.Usage?.InputTokenCount ?? 0,
            response.Usage?.OutputTokenCount ?? 0,
            response.Usage?.TotalTokenCount ?? 0,
            response.ModelId,
            clock.ElapsedMilliseconds);

        if (!response.TryGetResult(out var extracted))
        {
            return new JdExtractionOutcome(
                AgentName, reply, Requirements: null,
                "The model's reply did not parse as the JdRequirements schema.");
        }

        return new JdExtractionOutcome(
            AgentName, reply, VerifyEvidence(jobDescription, extracted), FaultDetail: null);
    }

    /// <summary>Checked, never trusted: each evidence span must appear verbatim in the JD
    /// (modulo whitespace runs and case, the interview-kit rule). A missing or unverifiable
    /// span marks the requirement <c>inferred</c> — kept, badged, never silently stripped.</summary>
    private static JdRequirements VerifyEvidence(string jobDescription, JdRequirements extracted)
    {
        var haystack = CollapseWhitespace(jobDescription);
        var verified = extracted.Requirements
            .Select(r => r with { Inferred = r.Inferred || !SpanExists(r.EvidenceSpan, haystack) })
            .ToList();
        return extracted with { Requirements = verified };
    }

    private static bool SpanExists(string? span, string haystack)
        => !string.IsNullOrWhiteSpace(span)
           && haystack.Contains(CollapseWhitespace(span), StringComparison.OrdinalIgnoreCase);

    private static string CollapseWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
