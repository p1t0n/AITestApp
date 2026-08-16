using System.Text.Json;
using CvManager.Agents.Staffing;

namespace CvManager.Agents.Agents;

/// <summary>What one match run produced: the gap-analysis markdown, the parsed deterministic
/// facts (score/band — from the structured verdict, or the legacy regex fallback; null when the
/// answer stated none), plus the reply carrying the token usage the caller meters under
/// <see cref="AgentName"/>.</summary>
public sealed record MatchRunOutcome(
    string AgentName, string Answer, AgentReply Reply, int? Score = null, string? Band = null);

/// <summary>The match step seam: lets the staffing pipeline consume the run service while its
/// tests substitute a fake (the real service needs a live agent stack).</summary>
public interface IMatchRunService
{
    /// <summary>Runs the agent for one employee/job-description pair. When the caller already
    /// holds the JD's structured extraction (P1T-117: one extraction per JD), it rides into the
    /// prompt so the model assesses against the extracted requirements instead of re-reading
    /// the raw JD from scratch.</summary>
    Task<MatchRunOutcome> RunAsync(
        Guid employeeId, string jobDescription, JdRequirements? requirements = null, CancellationToken ct = default);
}

/// <summary>
/// The core of a match run, extracted from the POST /agents/match endpoint: build the prompt from
/// the typed fields (the template lives here, as the single source of truth) and run the match
/// agent. No HTTP types, no cap-check, no metering: those stay with the caller, which orchestrates
/// them differently per surface.
///
/// Upstream-fault mapping to HTTP lives in the endpoint shell, not here. This service lets
/// <see cref="HttpRequestException"/> from the model/MCP stack propagate; the shell turns it into
/// a 502.
/// </summary>
public sealed class MatchRunService : IMatchRunService
{
    private readonly IChatAgent _agent;

    public MatchRunService(IChatAgent agent) => _agent = agent;

    /// <summary>Runs the agent for one employee/job-description pair.</summary>
    public async Task<MatchRunOutcome> RunAsync(
        Guid employeeId, string jobDescription, JdRequirements? requirements = null, CancellationToken ct = default)
    {
        var prompt = $"Assess employee {employeeId} against this job description:\n\n{jobDescription}";
        if (requirements is not null)
        {
            prompt += $"\n\n{requirements.ToPromptBlock()}";
        }

        var reply = await _agent.AskAsync(prompt, ct);

        // Structured verdict first (the wire is schema-constrained since P1T-118); the legacy
        // regex parser stays as the fallback for a non-JSON reply, so the answer ships either way.
        if (TryParseAssessment(reply.Text) is { } assessment)
        {
            return new MatchRunOutcome(
                _agent.Name,
                assessment.GapAnalysisMarkdown,
                reply,
                assessment.Score is >= 0 and <= 100 ? assessment.Score : null,
                assessment.Band?.ToDisplay());
        }

        var facts = MatchAnswerParser.Parse(reply.Text);
        return new MatchRunOutcome(_agent.Name, reply.Text, reply, facts.Score, facts.Band);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static MatchAssessment? TryParseAssessment(string text)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<MatchAssessment>(text.Trim(), Json);
            return string.IsNullOrWhiteSpace(parsed?.GapAnalysisMarkdown) ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
