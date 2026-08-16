namespace CvManager.Domain.Entities;

/// <summary>
/// One Roster Scan run (P1T-122): a durable Scoring Job that survives restarts and pauses.
/// Pausing is the normal path, not an edge case — free-tier quota windows (RPD resets) and user
/// token caps both park the job with a <see cref="ResumeAt"/> instead of failing it. Partial
/// progress is always visible: results land per candidate row as chunks complete.
/// </summary>
public class ScoringJob
{
    public Guid Id { get; set; }

    /// <summary>Who submitted the scan. Null when the run was unattributed.</summary>
    public Guid? RequestedByUserId { get; set; }

    public string JobDescription { get; set; } = string.Empty;

    /// <summary>The JdRequirements extraction the whole run scores against, serialized JSON —
    /// extracted once at intake (one extraction per JD) and reused by every chunk. Null until
    /// intake ran. Stored as jsonb on PostgreSQL.</summary>
    public string? ExtractionJson { get; set; }

    /// <summary>The request's pre-filters snapshot (availability/skills/location/minYears),
    /// serialized JSON. Stored as jsonb on PostgreSQL.</summary>
    public string? FiltersJson { get; set; }

    /// <summary>One of <see cref="ScoringJobState"/>.</summary>
    public string State { get; set; } = ScoringJobState.Queued;

    /// <summary>One of <see cref="ScoringJobPauseReason"/>; set only while paused.</summary>
    public string? PauseReason { get; set; }

    /// <summary>When a paused job becomes due to resume (quota window / cap window reset).</summary>
    public DateTimeOffset? ResumeAt { get; set; }

    /// <summary>Candidates per scoring chunk (model call), frozen at intake.</summary>
    public int ChunkSize { get; set; }

    /// <summary>Why the job failed terminally; null otherwise.</summary>
    public string? FailureDetail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ScoringJobCandidate> Candidates { get; set; } = [];
}

/// <summary>One employee's slot in a scan. Score/band/rationale come from the structured chunk
/// verdict; <see cref="Scorable"/> false with nulls is the honest "the digest gave nothing to
/// judge" outcome — represented, never invented.</summary>
public class ScoringJobCandidate
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid EmployeeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>One of <see cref="ScoringCandidateStatus"/>.</summary>
    public string Status { get; set; } = ScoringCandidateStatus.Pending;

    public int? Score { get; set; }

    public string? Band { get; set; }

    public string? Rationale { get; set; }

    public bool? Scorable { get; set; }

    /// <summary>Set only when this candidate's scoring failed (chunk fault, missing from the
    /// reply); the job itself keeps going.</summary>
    public string? Error { get; set; }
}

/// <summary>The pinned job states. Terminal: completed, failed.</summary>
public static class ScoringJobState
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Why a job paused: the shared model quota (RPD/RPM) or the requesting user's cap.</summary>
public static class ScoringJobPauseReason
{
    public const string Quota = "quota";
    public const string Cap = "cap";
}

/// <summary>The pinned candidate statuses. Failed counts as settled — progress always ends at
/// total/total, mirroring the staffing stepper's rule.</summary>
public static class ScoringCandidateStatus
{
    public const string Pending = "pending";
    public const string Scored = "scored";
    public const string Failed = "failed";
}
