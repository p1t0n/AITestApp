using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Usage;
using CvManager.Application.Search;
using CvManager.Domain.Entities;

namespace CvManager.Agents.RosterScan;

/// <summary>What one runner pass over a job ended as.</summary>
public enum RosterScanRunResult
{
    Completed,
    Paused,
    Failed,
}

/// <summary>
/// The orchestration core of one Roster Scan job (P1T-124), scoped per pass: intake (extract the
/// JD once + sweep the roster digests into candidate rows), then chunk through the scoring
/// transport, batch-writing results so partial progress is durable. Pause/resume is the normal
/// path: the transport's quota exception parks the job until the RPD window resets (midnight
/// Pacific), a tripped user cap parks it until that window resets — nothing re-scores on resume,
/// only pending rows are worked. A non-quota fault fails the job with detail; per-candidate
/// failures never fail the job.
/// </summary>
public sealed class RosterScanRunner(
    ScoringJobStore store,
    IJdRequirementExtractor extractor,
    IRosterDigestSource digests,
    IScoringTransport transport,
    IUsageMeter meter,
    IUsageService usage,
    RosterScanOptions options,
    TimeProvider clock,
    ILogger<RosterScanRunner> logger)
{
    public const string AgentName = "roster-scan";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Runs one pass over a queued job until it completes, pauses, or fails.</summary>
    public async Task<RosterScanRunResult> RunAsync(ScoringJob job, CancellationToken ct = default)
    {
        if (!await store.TryTransitionAsync(job.Id, ScoringJobState.Running, ct: ct))
        {
            logger.LogWarning("Scoring job {JobId} could not move to running (state {State}); skipping.",
                job.Id, job.State);
            return RosterScanRunResult.Failed;
        }

        try
        {
            var extraction = await IntakeAsync(job, ct);
            return await ScoreAsync(job, extraction, ct);
        }
        catch (ScoringQuotaExceededException ex)
        {
            var resumeAt = NextQuotaReset(clock.GetUtcNow());
            logger.LogInformation(ex, "Scoring job {JobId} paused on quota until {ResumeAt}.", job.Id, resumeAt);
            await store.TryTransitionAsync(job.Id, ScoringJobState.Paused,
                ScoringJobPauseReason.Quota, resumeAt, ct: ct);
            return RosterScanRunResult.Paused;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scoring job {JobId} failed.", job.Id);
            await store.TryTransitionAsync(job.Id, ScoringJobState.Failed, failureDetail: ex.Message, ct: ct);
            return RosterScanRunResult.Failed;
        }
    }

    /// <summary>Extraction + candidate materialization, both idempotent: a resumed job re-enters
    /// here and skips whatever intake already persisted.</summary>
    private async Task<JdRequirements?> IntakeAsync(ScoringJob job, CancellationToken ct)
    {
        JdRequirements? extraction = null;
        if (job.ExtractionJson is { Length: > 0 } persisted)
        {
            extraction = JsonSerializer.Deserialize<JdRequirements>(persisted, Json);
        }
        else
        {
            var outcome = await extractor.ExtractAsync(job.JobDescription, ct);
            await MeterAsync(job, JdRequirementExtractor.AgentName, outcome.Reply, ct);
            if (outcome.Requirements is null)
            {
                throw new InvalidOperationException(
                    $"JD requirement extraction failed: {outcome.FaultDetail}");
            }

            extraction = outcome.Requirements;
            await store.SetExtractionAsync(job.Id, JsonSerializer.Serialize(extraction, Json), ct);
        }

        if (job.Candidates.Count == 0 && !await HasCandidatesAsync(job.Id, ct))
        {
            await SweepDigestsAsync(job.Id, ct);
        }

        return extraction;
    }

    private async Task<bool> HasCandidatesAsync(Guid jobId, CancellationToken ct)
        => (await store.GetPendingCandidatesAsync(jobId, 1, ct)).Count > 0
           || (await store.GetAsync(jobId, ct))?.Candidates.Count > 0;

    private async Task SweepDigestsAsync(Guid jobId, CancellationToken ct)
    {
        for (var page = 1; ; page++)
        {
            var digestPage = await digests.ListAsync(page, EmployeeDigestService.DefaultPageSize, ct)
                ?? throw new InvalidOperationException("The roster_digest_list result was unreadable.");
            if (digestPage.Items.Count > 0)
            {
                await store.AddCandidatesAsync(jobId, digestPage.Items
                    .Select(d => new ScoringCandidateSeed(d.EmployeeId, d.Name, d.Title, d.Digest))
                    .ToList(), ct);
            }

            if (page * digestPage.PageSize >= digestPage.Total || digestPage.Items.Count == 0)
            {
                return;
            }
        }
    }

    private async Task<RosterScanRunResult> ScoreAsync(
        ScoringJob job, JdRequirements? extraction, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Cap re-check before every chunk: the scan respects caps like staffing does — it
            // just pauses instead of degrading.
            if (job.RequestedByUserId is { } userId
                && await usage.FindExceededAsync(userId, ct) is { } exceeded)
            {
                await store.TryTransitionAsync(job.Id, ScoringJobState.Paused,
                    ScoringJobPauseReason.Cap, exceeded.ResetAt, ct: ct);
                logger.LogInformation(
                    "Scoring job {JobId} paused on the {Window} cap until {ResumeAt}.",
                    job.Id, exceeded.Window, exceeded.ResetAt);
                return RosterScanRunResult.Paused;
            }

            var pending = await store.GetPendingCandidatesAsync(job.Id, options.ChunkSize, ct);
            if (pending.Count == 0)
            {
                await store.TryTransitionAsync(job.Id, ScoringJobState.Completed, ct: ct);
                return RosterScanRunResult.Completed;
            }

            var chunk = pending
                .Select(c => new EmployeeDigest(c.EmployeeId, c.Name, c.Title, c.Digest))
                .ToList();
            var scored = await transport.ScoreChunkAsync(job.JobDescription, extraction, chunk, ct);
            await MeterAsync(job, AgentName, scored.Reply, ct);
            await store.WriteChunkResultsAsync(job.Id, scored.Results, ct);
        }
    }

    private async Task MeterAsync(ScoringJob job, string agentName, AgentReply reply, CancellationToken ct)
    {
        // Unattributed jobs run unmetered and uncapped, mirroring the one-shot endpoints.
        if (job.RequestedByUserId is { } userId)
        {
            await meter.RecordAsync(userId, agentName, reply, ct: ct);
        }
    }

    /// <summary>The free-tier RPD window resets at midnight Pacific (P1T-107 research).</summary>
    public static DateTimeOffset NextQuotaReset(DateTimeOffset nowUtc)
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, pacific);
        var nextMidnightLocal = localNow.Date.AddDays(1);
        var offset = pacific.GetUtcOffset(nextMidnightLocal);
        return new DateTimeOffset(nextMidnightLocal, offset).ToUniversalTime();
    }
}
