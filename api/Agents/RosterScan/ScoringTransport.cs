using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using Microsoft.Extensions.AI;
using Polly;

namespace ExpertToJob.Agents.RosterScan;

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
        IReadOnlyList<ExpertDigest> chunk,
        CancellationToken ct = default);
}

/// <summary>The model quota (RPM/RPD) is exhausted beyond the retry budget. Not a failure — the
/// runner parks the job and resumes when the window resets.</summary>
public sealed class ScoringQuotaExceededException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Roster Scan knobs. The shipped quota numbers leave headroom under the pinned model's
/// free-tier limit (gemini-3.5-flash-lite: RPM 15 / RPD 500, P1T-114).
///
/// <para>Since EXP-107 the two quota knobs carry no property default: they are the pair an
/// operator has to move the day the key changes tier, so the shipped values live once, in
/// <c>api/Agents/appsettings.json</c>, where they are visible next to the host that reads them.
/// Both are validated with <c>ValidateOnStart</c>. The pacing and retry knobs beside them keep
/// their code defaults — nothing ever set those, and they are tuning of the transport rather than
/// an operator's decision.</para></summary>
public sealed class RosterScanOptions
{
    public const string Section = "RosterScan";

    /// <summary>Candidates per scoring chunk (one model call each).</summary>
    public int ChunkSize { get; set; } = 10;

    /// <summary>Pacing budget for the shared limiter. From configuration only (EXP-107).</summary>
    [Range(1, int.MaxValue, ErrorMessage = $"{Section}:{nameof(RequestsPerMinute)} must be greater than 0.")]
    public int RequestsPerMinute { get; set; }

    /// <summary>Attempts per chunk before a 429 is treated as quota exhaustion.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base for the exponential retry backoff (base, 2×base, 4×base…).</summary>
    public double RetryBaseSeconds { get; set; } = 2;

    /// <summary>How often the worker sweeps for due paused / orphaned jobs. A constant, not a knob
    /// (EXP-106): nothing ever set it, and it is a liveness detail of the worker rather than an
    /// operator's decision — unlike the quota numbers beside it, which an operator may genuinely
    /// need to move without a deploy.</summary>
    public const double ResumeSweepSeconds = 30;

    /// <summary>The day's call budget the submit estimate is judged against (the pinned model's
    /// free-tier RPD, P1T-114). From configuration only (EXP-107).</summary>
    [Range(1, int.MaxValue, ErrorMessage = $"{Section}:{nameof(RequestsPerDay)} must be greater than 0.")]
    public int RequestsPerDay { get; set; }
}

/// <summary>
/// The free-tier default transport: a tool-less, schema-constrained chat call per chunk, paced by
/// a shared <see cref="RateLimiter"/> and retried with exponential backoff on 429s (bounded — the
/// budget spent, a typed quota exception surfaces). Honesty end to end: the prompt gives the model
/// the <c>scorable: false</c> outlet, and the reply is checked, never trusted — unknown expert
/// ids are dropped, chunk members missing from the reply fail honestly, out-of-range scores null.
/// </summary>
public sealed class QueuedSyncScoringTransport : IScoringTransport
{
    private const string Instructions =
        """
        You score candidates against a job description for a first-pass roster scan. You are given
        the job description (and, when available, its extracted requirements) plus a list of
        candidate career digests. Reply with the structured object: exactly one assessment per
        candidate, using exactly the expertId values given.

        Rules, in priority order:
        1. Judge ONLY from each candidate's digest — never invent skills, experience, or facts a
           digest does not contain.
        2. score is 0-100 against the requirements; band is Strong (>=75), Moderate (50-74),
           Weak (25-49), or InsufficientEvidence.
        3. When a digest gives you nothing to judge against the requirements, set scorable to
           false and score and band to null — never guess a number.
        4. rationale is one or two sentences grounded in the digest.
        """;

    private readonly IChatClient _chat;
    private readonly RateLimiter _limiter;
    private readonly ResiliencePipeline _retry;

    public QueuedSyncScoringTransport(
        IChatClient chat, RateLimiter limiter, RosterScanOptions options, TimeProvider clock)
    {
        _chat = chat;
        _limiter = limiter;
        _retry = RateLimitRetry.Build(
            options.MaxRetryAttempts,
            TimeSpan.FromSeconds(options.RetryBaseSeconds),
            DelayBackoffType.Exponential,
            clock);
    }

    public async Task<ScoredChunk> ScoreChunkAsync(
        string jobDescription,
        JdRequirements? extraction,
        IReadOnlyList<ExpertDigest> chunk,
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

    /// <summary>One chunk's model call on the shipped 429 ladder (budget and backoff in
    /// <see cref="RateLimitRetry"/>). Every attempt takes its own permit from the shared pacer, and
    /// a 429 that outlives the budget becomes the typed quota exception the runner parks on.</summary>
    private async Task<(ChatResponse Response, string? ModelId, long LatencyMs, int Iterations, string? ToolSequence)> CallWithPacingAndRetryAsync(
        string prompt, ChatOptions options, CancellationToken ct)
    {
        var attempts = 0;
        try
        {
            return await _retry.ExecuteAsync(
                async token =>
                {
                    attempts++;
                    using var lease = await _limiter.AcquireAsync(1, token);
                    using var metering = Usage.MeteringScope.Begin();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var response = await _chat.GetResponseAsync(
                        [new ChatMessage(ChatRole.System, Instructions), new ChatMessage(ChatRole.User, prompt)],
                        options,
                        token);
                    var run = metering.Snapshot();
                    return (response, run.ModelId,
                        run.LatencyMs > 0 ? run.LatencyMs : clock.ElapsedMilliseconds,
                        run.Iterations, run.ToolSequence);
                },
                ct);
        }
        catch (Exception ex) when (RateLimitRetry.IsRateLimit(ex))
        {
            throw new ScoringQuotaExceededException(
                $"The model quota is exhausted ({attempts} attempts hit 429).", ex);
        }
    }

    private static string BuildPrompt(
        string jobDescription, JdRequirements? extraction, IReadOnlyList<ExpertDigest> chunk)
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
        prompt.AppendLine(JsonSerializer.Serialize(chunk, JsonSerializerOptions.Web));
        return prompt.ToString();
    }

    /// <summary>Checked, never trusted: every chunk member gets exactly one result row.</summary>
    private static List<ScoringCandidateResult> MapResults(
        IReadOnlyList<ExpertDigest> chunk, string replyText)
    {
        var assessments = TryParse(replyText)?.Assessments
            ?.Where(a => a is not null)
            .Select(a => a!)
            .ToLookup(a => a.ExpertId);

        var results = new List<ScoringCandidateResult>(chunk.Count);
        foreach (var candidate in chunk)
        {
            var assessment = assessments?[candidate.ExpertId].FirstOrDefault();
            if (assessment is null)
            {
                results.Add(new ScoringCandidateResult(
                    candidate.ExpertId, new ScoringCandidateStatus.Failed(),
                    null, null, null, null,
                    assessments is null
                        ? "The chunk reply did not parse as the scoring schema."
                        : "The model's chunk reply did not assess this candidate."));
                continue;
            }

            results.Add(new ScoringCandidateResult(
                candidate.ExpertId,
                new ScoringCandidateStatus.Scored(),
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
            return JsonSerializer.Deserialize<ChunkAssessments>(text.Trim(), JsonSerializerOptions.Web);
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
        [property: JsonPropertyName("expertId")] Guid ExpertId,
        [property: JsonPropertyName("score")] int? Score,
        [property: JsonPropertyName("band")] MatchBand? Band,
        [property: JsonPropertyName("rationale")] string? Rationale,
        [property: JsonPropertyName("scorable")] bool Scorable);
}
