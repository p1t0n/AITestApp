using System.Text.Json.Serialization;
using ExpertToJob.Domain.Entities;

namespace ExpertToJob.Agents.RosterScan;

/// <summary>POST /agents/roster-scan body — same optional filters as shortlist/staffing.</summary>
public sealed record RosterScanRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null);

/// <summary>The submit-time expectation, honest about the quota arithmetic: how many candidates
/// the scan covers, how many model calls that takes, and the day's call budget it draws from.</summary>
public sealed record RosterScanEstimate(int Candidates, int Calls, int RpdBudget);

/// <summary>202 body: the job to poll plus the estimate.</summary>
public sealed record RosterScanAccepted(Guid JobId, RosterScanEstimate Estimate);

/// <summary>One candidate row in the polling payload. Score/band/rationale are the structured
/// chunk verdict; <c>scorable: false</c> with nulls is the honest "nothing to judge" outcome.</summary>
public sealed record RosterScanCandidateView(
    Guid EmployeeId,
    string Name,
    string Title,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Score,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Band,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rationale,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Scorable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);

/// <summary>GET /agents/roster-scan/{id} — the polling contract (no SSE: jobs span hours and
/// pauses). Candidates arrive scored-by-score-desc, then failed, then pending.</summary>
public sealed record RosterScanJobView(
    Guid JobId,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PauseReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ResumeAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureDetail,
    DateTimeOffset CreatedAt,
    string JobDescription,
    ScoringJobProgress Progress,
    IReadOnlyList<RosterScanCandidateView> Candidates)
{
    public static RosterScanJobView Of(ScoringJob job) => new(
        job.Id, job.State, job.PauseReason, job.ResumeAt, job.FailureDetail,
        job.CreatedAt, job.JobDescription,
        ScoringJobProgress.Of(job.Candidates),
        job.Candidates.Select(c => new RosterScanCandidateView(
            c.EmployeeId, c.Name, c.Title, c.Status, c.Score, c.Band, c.Rationale, c.Scorable, c.Error)).ToList());
}

/// <summary>GET /agents/roster-scan — one light row per job, newest-first.</summary>
public sealed record RosterScanJobSummary(
    Guid JobId,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PauseReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ResumeAt,
    DateTimeOffset CreatedAt,
    string JobDescription,
    ScoringJobProgress Progress);
