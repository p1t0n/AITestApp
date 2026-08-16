using CvManager.Agents.Agents;

namespace CvManager.Agents.Staffing;

/// <summary>The typed staffing request: the job description plus the optional shortlist filters,
/// and how many top candidates to fan the match step out over (default 3, clamped to 1..5).</summary>
public sealed record StaffingPipelineRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null,
    int? MatchTop = null);

/// <summary>One shortlisted candidate's retrieval facts, lifted from the shortlist run response.</summary>
public sealed record StaffingShortlistDetail(
    double Score,
    ShortlistCoverage Coverage,
    IReadOnlyList<ShortlistRequirementItem> Requirements);

/// <summary>One candidate's match step result. <see cref="Status"/> is one of
/// <see cref="StaffingMatchStatus"/>; score/band are parsed from the answer markdown (null when
/// unreadable — the markdown ships regardless); <see cref="Error"/> is set only on failure.</summary>
public sealed record StaffingMatchDetail(
    string Status,
    int? Score,
    string? Band,
    string? Answer,
    string? Error);

/// <summary>The pinned match step statuses (P1T-71).</summary>
public static class StaffingMatchStatus
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>One candidate in the staffing report: identity and shortlist facts are deterministic
/// (from the shortlist tool result); the match detail comes from that candidate's match run; the
/// rationale comes from the narrative step, or a deterministic template when it degrades.</summary>
public sealed record StaffingCandidate(
    Guid EmployeeId,
    string Name,
    string Title,
    StaffingShortlistDetail Shortlist,
    StaffingMatchDetail Match,
    string Rationale);

/// <summary>The narrative step's validated pick: always one of the report's candidates.</summary>
public sealed record StaffingRecommendation(Guid EmployeeId, string Narrative);

/// <summary>The pinned POST /agents/staffing report (P1T-71, camelCase over the wire).
/// <see cref="Recommendation"/> is null when the narrative degrades; <see cref="Degraded"/> plus
/// <see cref="Notes"/> explain any partial results (failed matches, cap trips, narrative faults).
/// <see cref="ProposalId"/> (P1T-100) references the pending approval record created from this
/// run; omitted from the wire when persistence degraded, keeping the pre-P1T-100 payload
/// byte-identical.</summary>
public sealed record StaffingReport(
    IReadOnlyList<string> Requirements,
    IReadOnlyList<StaffingCandidate> Candidates,
    StaffingRecommendation? Recommendation,
    bool Degraded,
    IReadOnlyList<string> Notes,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? ProposalId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CvManager.Agents.Agents.JdRequirements? Extraction = null);

/// <summary>One ordered progress event from a pipeline run. This is the streaming seam: the
/// pipeline emits these in order (via <see cref="IProgress{T}"/> and on the outcome) and the SSE
/// endpoint maps them to wire events (see <c>StaffingSse</c>). Events that mark a UI-visible step
/// transition carry a <see cref="Status"/> (<c>started</c>/<c>completed</c>/<c>failed</c>); the
/// rest are message-only diagnostics. Match-step events additionally carry the candidate's name
/// and the k/N fan-out progress counters, where <see cref="CompletedCount"/> counts every finished
/// match run — failed ones included — so progress always ends at N/N.</summary>
public sealed record StaffingProgressEvent(
    int Sequence,
    string Stage,
    string Message,
    Guid? EmployeeId = null,
    string? Status = null,
    string? CandidateName = null,
    int? CompletedCount = null,
    int? TotalCount = null,
    string? Error = null);

/// <summary>The pinned <see cref="StaffingProgressEvent.Status"/> values.</summary>
public static class StaffingStepStatus
{
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// What one pipeline run produced. Exactly one of <see cref="Report"/> and
/// <see cref="ShortlistFault"/> is non-null: everything downstream of a successful shortlist
/// degrades into the report (never throws), but without a shortlist there is nothing to report,
/// so that one failure surfaces as data for the endpoint to map (502).
/// </summary>
public sealed record StaffingRunOutcome(
    StaffingReport? Report,
    string? ShortlistFault,
    IReadOnlyList<StaffingProgressEvent> Events);
