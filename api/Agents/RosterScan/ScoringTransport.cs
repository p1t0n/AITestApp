using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CvManager.Agents.Agents;
using CvManager.Agents.Staffing;
using CvManager.Application.Search;
using CvManager.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.RosterScan;

/// <summary>One settled chunk: per-candidate results (every chunk member accounted for) plus the
/// reply the caller meters under <c>roster-scan</c>.</summary>
public sealed record ScoredChunk(IReadOnlyList<ScoringCandidateResult> Results, AgentReply Reply);

/// <summary>
/// The sync-vs-batch seam (P1T-123, knowledge item 3): the Roster Scan runner scores chunks
/// through this interface and never knows the transport. The free-tier default is
/// <see cref="QueuedSyncScoringTransport"/> (client-side queued sync with rate pacing); a real
/// Gemini Batch transport (Tier 1 key, <c>Google.GenAI client.Batches</c> — async submit, ~24h
/// window, 50% price) slots in here without touching the runner. See
/// <c>manuals/gemini-batch-api.md</c> for the selection facts.
/// </summary>
public interface IScoringTransport
{
    /// <summary>Scores one chunk of candidate digests against the JD (and its structured
    /// extraction when available). Throws <see cref="ScoringQuotaExceededException"/> when the
    /// model quota is exhausted beyond the retry budget — the runner maps that to paused(quota).</summary>
    Task<ScoredChunk> ScoreChunkAsync(
        string jobDescription,
        JdRequirements? extraction,
        IReadOnlyList<EmployeeDigest> chunk,
        CancellationToken ct = default);
}

/// <summary>The model quota (RPM/RPD) is exhausted beyond the retry budget. Not a failure — the
/// runner parks the job and resumes when the window resets.</summary>
public sealed class ScoringQuotaExceededException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Roster Scan knobs. RPM defaults leave headroom under the pinned model's free-tier
/// limit (gemini-3.5-flash-lite: RPM 15 / RPD 500, P1T-114).</summary>
public sealed class RosterScanOptions
{
    public const string Section = "RosterScan";

    /// <summary>Candidates per scoring chunk (one model call each).</summary>
    public int ChunkSize { get; set; } = 10;

    /// <summary>Pacing budget for the shared limiter.</summary>
    public int RequestsPerMinute { get; set; } = 12;

    /// <summary>Attempts per chunk before a 429 is treated as quota exhaustion.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base for the exponential retry backoff (base, 2×base, 4×base…).</summary>
    public double RetryBaseSeconds { get; set; } = 2;

    /// <summary>How often the worker sweeps for due paused / orphaned jobs.</summary>
    public double ResumeSweepSeconds { get; set; } = 30;

    /// <summary>The day's call budget the submit estimate is judged against (the pinned model's
    /// free-tier RPD, P1T-114).</summary>
    public int RequestsPerDay { get; set; } = 500;
}

/// <summary>
/// The free-tier default transport: a tool-less, schema-constrained chat call per chunk, paced by
/// a shared <see cref="RateLimiter"/> and retried with exponential backoff on 429s (bounded — the
/// budget spent, a typed quota exception surfaces). Honesty end to end: the prompt gives the model
/// the <c>scorable: false</c> outlet, and the reply is checked, never trusted — unknown employee
/// ids are dropped, chunk members missing from the reply fail honestly, out-of-range scores null.
/// </summary>
public sealed class QueuedSyncScoringTransport : IScoringTransport
{
    private const string Instructions =
        """
        You score candidates against a job description for a first-pass roster scan. You are given
        the job description (and, when available, its extracted requirements) plus a list of
        candidate career digests. Reply with the structured object: exactly one assessment per
        candidate, using exactly the employeeId values given.

        Rules, in priority order:
        1. Judge ONLY from each candidate's digest — never invent skills, experience, or facts a
           digest does not contain.
        2. score is 0-100 against the requirements; band is Strong (>=75), Moderate (50-74),
           Weak (25-49), or InsufficientEvidence.
        3. When a digest gives you nothing to judge against the requirements, set scorable to
           false and score and band to null — never guess a number.
        4. rationale is one or two sentences grounded in the digest.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IChatClient _chat;
    private readonly RateLimiter _limiter;
    private readonly RosterScanOptions _options;
    private readonly TimeProvider _clock;

    public QueuedSyncScoringTransport(
        IChatClient chat, RateLimiter limiter, RosterScanOptions options, TimeProvider clock)
    {
        _chat = chat;
        _limiter = limiter;
        _options = options;
        _clock = clock;
    }

    public async Task<ScoredChunk> ScoreChunkAsync(
        string jobDescription,
        JdRequirements? extraction,
        IReadOnlyList<EmployeeDigest> chunk,
        CancellationToken ct = default)
    {
        var prompt = BuildPrompt(jobDescription, extraction, chunk);
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                AIJsonUtilities.CreateJsonSchema(typeof(ChunkAssessments)), "roster_scan_chunk"),
        };

        var call = await CallWithPacingAndRetryAsync(prompt, options, ct);
        var reply = ToReply(call.Response, call.ModelId, call.LatencyMs, call.Iterations, call.ToolSequence);
        return new ScoredChunk(MapResults(chunk, call.Response.Text), reply);
    }

    private async Task<(ChatResponse Response, string? ModelId, long LatencyMs, int Iterations, string? ToolSequence)> CallWithPacingAndRetryAsync(
        string prompt, ChatOptions options, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var lease = await _limiter.AcquireAsync(1, ct);
            try
            {
                using var metering = Usage.MeteringScope.Begin();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var response = await _chat.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, Instructions), new ChatMessage(ChatRole.User, prompt)],
                    options,
                    ct);
                var run = metering.Snapshot();
                return (response, run.ModelId,
                    run.LatencyMs > 0 ? run.LatencyMs : clock.ElapsedMilliseconds,
                    run.Iterations, run.ToolSequence);
            }
            catch (Exception ex) when (StaffingRetryPolicy.IsRateLimit(ex))
            {
                if (attempt >= _options.MaxRetryAttempts)
                {
                    throw new ScoringQuotaExceededException(
                        $"The model quota is exhausted ({attempt} attempts hit 429).", ex);
                }

                var delay = TimeSpan.FromSeconds(_options.RetryBaseSeconds * Math.Pow(2, attempt - 1));
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _clock, ct);
                }
            }
        }
    }

    private static string BuildPrompt(
        string jobDescription, JdRequirements? extraction, IReadOnlyList<EmployeeDigest> chunk)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Job description:");
        prompt.AppendLine(jobDescription);
        if (extraction is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine(extraction.ToPromptBlock());
        }

        prompt.AppendLine();
        prompt.AppendLine("Candidate digests:");
        prompt.AppendLine(JsonSerializer.Serialize(chunk, Json));
        return prompt.ToString();
    }

    /// <summary>Checked, never trusted: every chunk member gets exactly one result row.</summary>
    private static List<ScoringCandidateResult> MapResults(
        IReadOnlyList<EmployeeDigest> chunk, string replyText)
    {
        var assessments = TryParse(replyText)?.Assessments
            ?.Where(a => a is not null)
            .Select(a => a!)
            .ToLookup(a => a.EmployeeId);

        var results = new List<ScoringCandidateResult>(chunk.Count);
        foreach (var candidate in chunk)
        {
            var assessment = assessments?[candidate.EmployeeId].FirstOrDefault();
            if (assessment is null)
            {
                results.Add(new ScoringCandidateResult(
                    candidate.EmployeeId, ScoringCandidateStatus.Failed,
                    null, null, null, null,
                    assessments is null
                        ? "The chunk reply did not parse as the scoring schema."
                        : "The model's chunk reply did not assess this candidate."));
                continue;
            }

            results.Add(new ScoringCandidateResult(
                candidate.EmployeeId,
                ScoringCandidateStatus.Scored,
                assessment.Score is >= 0 and <= 100 ? assessment.Score : null,
                assessment.Band?.ToDisplay(),
                assessment.Rationale,
                assessment.Scorable,
                Error: null));
        }

        return results;
    }

    private static ChunkAssessments? TryParse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ChunkAssessments>(text.Trim(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentReply ToReply(
        ChatResponse response, string? modelId, long latencyMs, int iterations, string? toolSequence) => new(
        response.Text,
        response.Usage?.InputTokenCount ?? 0,
        response.Usage?.OutputTokenCount ?? 0,
        response.Usage?.TotalTokenCount ?? 0,
        modelId ?? response.ModelId,
        latencyMs,
        iterations,
        toolSequence);

    internal sealed record ChunkAssessments(
        [property: JsonPropertyName("assessments")] IReadOnlyList<ChunkAssessment?>? Assessments);

    internal sealed record ChunkAssessment(
        [property: JsonPropertyName("employeeId")] Guid EmployeeId,
        [property: JsonPropertyName("score")] int? Score,
        [property: JsonPropertyName("band")] MatchBand? Band,
        [property: JsonPropertyName("rationale")] string? Rationale,
        [property: JsonPropertyName("scorable")] bool Scorable);
}
