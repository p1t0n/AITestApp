using CvManager.Application.Abstractions;
using CvManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvManager.Agents.RosterScan;

/// <summary>One employee slot to seed (identity + the digest captured from the sweep).</summary>
public sealed record ScoringCandidateSeed(Guid EmployeeId, string Name, string Title, string Digest = "");

/// <summary>One candidate's settled result from a scoring chunk.</summary>
public sealed record ScoringCandidateResult(
    Guid EmployeeId,
    string Status,
    int? Score,
    string? Band,
    string? Rationale,
    bool? Scorable,
    string? Error);

/// <summary>Progress computed from candidate statuses. Failed counts as settled — progress always
/// ends at Total/Total, mirroring the staffing stepper's rule.</summary>
public sealed record ScoringJobProgress(int Scored, int Failed, int Pending, int Total)
{
    public int Settled => Scored + Failed;

    public static ScoringJobProgress Of(IEnumerable<ScoringJobCandidate> candidates)
    {
        int scored = 0, failed = 0, pending = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Status == ScoringCandidateStatus.Scored) scored++;
            else if (candidate.Status == ScoringCandidateStatus.Failed) failed++;
            else pending++;
        }

        return new ScoringJobProgress(scored, failed, pending, scored + failed + pending);
    }
}

/// <summary>
/// Persistence for Scoring Jobs (P1T-122): the durable record of one Roster Scan run. State
/// transitions are guarded here so a race (a resume timer and a restart-recovery sweep touching
/// the same job) can never resurrect a terminal job or double-run a queued one — an illegal
/// transition reports false and changes nothing. Chunk results are batch-written so partial
/// progress is durable the moment a chunk settles.
/// </summary>
public sealed class ScoringJobStore(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Which states each state may move to. Completed/failed are terminal.</summary>
    private static readonly Dictionary<string, string[]> LegalTransitions = new()
    {
        [ScoringJobState.Queued] = [ScoringJobState.Running, ScoringJobState.Failed],
        [ScoringJobState.Running] =
            [ScoringJobState.Paused, ScoringJobState.Completed, ScoringJobState.Failed, ScoringJobState.Queued],
        [ScoringJobState.Paused] = [ScoringJobState.Queued, ScoringJobState.Running],
        [ScoringJobState.Completed] = [],
        [ScoringJobState.Failed] = [],
    };

    /// <summary>Creates a queued job with its pending candidate rows. Unlike proposal creation
    /// this is not best-effort: the submit endpoint returns the job id, so a persistence fault
    /// must surface (the endpoint shell maps it).</summary>
    public async Task<ScoringJob> CreateAsync(
        Guid? requestedBy,
        string jobDescription,
        string? extractionJson,
        string? filtersJson,
        int chunkSize,
        IReadOnlyList<ScoringCandidateSeed> candidates,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var job = new ScoringJob
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = requestedBy,
            JobDescription = jobDescription,
            ExtractionJson = extractionJson,
            FiltersJson = filtersJson,
            State = ScoringJobState.Queued,
            ChunkSize = chunkSize,
            CreatedAt = now,
            UpdatedAt = now,
            Candidates = candidates.Select(ToPendingRow).ToList(),
        };

        db.ScoringJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    /// <summary>Persists the intake extraction (one extraction per JD, reused by every chunk and
    /// by resumes).</summary>
    public async Task SetExtractionAsync(Guid jobId, string extractionJson, CancellationToken ct = default)
    {
        var job = await db.ScoringJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            return;
        }

        job.ExtractionJson = extractionJson;
        job.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds pending candidate rows from an intake sweep (idempotent per employee — a
    /// re-run intake never duplicates a row).</summary>
    public async Task AddCandidatesAsync(
        Guid jobId, IReadOnlyList<ScoringCandidateSeed> candidates, CancellationToken ct = default)
    {
        var existing = await db.ScoringJobCandidates
            .Where(c => c.JobId == jobId)
            .Select(c => c.EmployeeId)
            .ToListAsync(ct);
        var known = existing.ToHashSet();

        foreach (var seed in candidates.Where(s => !known.Contains(s.EmployeeId)))
        {
            var row = ToPendingRow(seed);
            row.JobId = jobId;
            db.ScoringJobCandidates.Add(row);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The next pending candidates in stable id order — one scoring chunk's worth.</summary>
    public async Task<List<ScoringJobCandidate>> GetPendingCandidatesAsync(
        Guid jobId, int take, CancellationToken ct = default)
        => await db.ScoringJobCandidates.AsNoTracking()
            .Where(c => c.JobId == jobId && c.Status == ScoringCandidateStatus.Pending)
            .OrderBy(c => c.EmployeeId)
            .Take(take)
            .ToListAsync(ct);

    private static ScoringJobCandidate ToPendingRow(ScoringCandidateSeed seed) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = seed.EmployeeId,
        Name = seed.Name,
        Title = seed.Title,
        Digest = seed.Digest,
        Status = ScoringCandidateStatus.Pending,
    };

    /// <summary>Attempts a guarded state transition. Returns false (and changes nothing) when the
    /// job is missing or the move is illegal. Pause metadata is set on a move to paused and
    /// cleared on any move away from it; failure detail only lands on a move to failed.</summary>
    public async Task<bool> TryTransitionAsync(
        Guid id,
        string toState,
        string? pauseReason = null,
        DateTimeOffset? resumeAt = null,
        string? failureDetail = null,
        CancellationToken ct = default)
    {
        var job = await db.ScoringJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null || !LegalTransitions.TryGetValue(job.State, out var allowed) || !allowed.Contains(toState))
        {
            return false;
        }

        job.State = toState;
        job.UpdatedAt = clock.GetUtcNow();
        if (toState == ScoringJobState.Paused)
        {
            job.PauseReason = pauseReason;
            job.ResumeAt = resumeAt;
        }
        else
        {
            job.PauseReason = null;
            job.ResumeAt = null;
        }

        if (toState == ScoringJobState.Failed)
        {
            job.FailureDetail = failureDetail;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Batch-writes one settled chunk. Rows are matched by employee id within the job;
    /// results for unknown employees are ignored (checked, never trusted).</summary>
    public async Task WriteChunkResultsAsync(
        Guid jobId, IReadOnlyList<ScoringCandidateResult> results, CancellationToken ct = default)
    {
        var ids = results.Select(r => r.EmployeeId).ToList();
        var rows = await db.ScoringJobCandidates
            .Where(c => c.JobId == jobId && ids.Contains(c.EmployeeId))
            .ToListAsync(ct);
        var byEmployee = rows.ToDictionary(r => r.EmployeeId);

        foreach (var result in results)
        {
            if (!byEmployee.TryGetValue(result.EmployeeId, out var row))
            {
                continue;
            }

            row.Status = result.Status;
            row.Score = result.Score;
            row.Band = result.Band;
            row.Rationale = result.Rationale;
            row.Scorable = result.Scorable;
            row.Error = result.Error;
        }

        var job = await db.ScoringJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is not null)
        {
            job.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Jobs the runner should pick back up: paused jobs whose resume time is due, plus
    /// queued jobs (fresh or re-queued), plus running jobs — after a restart a "running" row is
    /// an orphan by definition (the runner is in-process). The caller re-queues each via
    /// <see cref="TryTransitionAsync"/> before working it.</summary>
    public async Task<List<ScoringJob>> FindResumableAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await db.ScoringJobs
            .Where(j => j.State == ScoringJobState.Queued
                        || j.State == ScoringJobState.Running
                        || (j.State == ScoringJobState.Paused && j.ResumeAt != null && j.ResumeAt <= now))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>One job with its candidates (scored first by score desc, then failed, then
    /// pending) — the polling endpoint's shape.</summary>
    public async Task<ScoringJob?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var job = await db.ScoringJobs.AsNoTracking()
            .Include(j => j.Candidates)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        job?.Candidates.Sort((a, b) =>
        {
            var byStatus = StatusRank(a).CompareTo(StatusRank(b));
            return byStatus != 0 ? byStatus : (b.Score ?? -1).CompareTo(a.Score ?? -1);
        });
        return job;

        static int StatusRank(ScoringJobCandidate c) => c.Status switch
        {
            ScoringCandidateStatus.Scored => 0,
            ScoringCandidateStatus.Failed => 1,
            _ => 2,
        };
    }

    /// <summary>The requester's jobs, newest-first, without candidate rows (the light index).</summary>
    public async Task<List<ScoringJob>> ListAsync(Guid? requestedBy, CancellationToken ct = default)
        => await db.ScoringJobs.AsNoTracking()
            .Where(j => j.RequestedByUserId == requestedBy)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

    /// <summary>Progress per job in one grouped query — the list endpoint's counts.</summary>
    public async Task<Dictionary<Guid, ScoringJobProgress>> GetProgressAsync(
        IReadOnlyList<Guid> jobIds, CancellationToken ct = default)
    {
        var counts = await db.ScoringJobCandidates.AsNoTracking()
            .Where(c => jobIds.Contains(c.JobId))
            .GroupBy(c => new { c.JobId, c.Status })
            .Select(g => new { g.Key.JobId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return counts
            .GroupBy(x => x.JobId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int Of(string status) => g.Where(x => x.Status == status).Sum(x => x.Count);
                    var scored = Of(ScoringCandidateStatus.Scored);
                    var failed = Of(ScoringCandidateStatus.Failed);
                    var pending = Of(ScoringCandidateStatus.Pending);
                    return new ScoringJobProgress(scored, failed, pending, scored + failed + pending);
                });
    }
}
