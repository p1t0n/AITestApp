using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ExpertToJob.Domain.Status;
using ExpertToJob.Agents.Agents;

namespace ExpertToJob.Agents.Staffing;

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
    StaffingMatchStatus Status,
    int? Score,
    string? Band,
    string? Answer,
    string? Error);

/// <summary>The pinned match step statuses (P1T-71). Closed since EXP-76: the report's own
/// aggregation reads every case, and the strings are the ones already in stored packages.</summary>
[JsonConverter(typeof(ClosedStatusJsonConverter<StaffingMatchStatus>))]
public closed record StaffingMatchStatus : IClosedStatus<StaffingMatchStatus>
{
    public sealed record Completed : StaffingMatchStatus;

    public sealed record Failed : StaffingMatchStatus;

    public sealed record Skipped : StaffingMatchStatus;

    /// <inheritdoc/>
    public string Value => this switch
    {
        Completed => "completed",
        Failed => "failed",
        Skipped => "skipped",
    };

    /// <inheritdoc/>
    public static bool TryParse(string? value, [NotNullWhen(true)] out StaffingMatchStatus? status)
    {
        status = value switch
        {
            "completed" => new Completed(),
            "failed" => new Failed(),
            "skipped" => new Skipped(),
            // The one discard arm the closed set keeps: this switches over a string read back
            // from a column or a payload, not over the hierarchy, so "none of them" is a real
            // case and the caller decides whether it degrades or throws.
            _ => null,
        };
        return status is not null;
    }

    /// <inheritdoc/>
    public static StaffingMatchStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new FormatException($"'{value}' is not a match status.");

    public sealed override string ToString() => Value;
}

/// <summary>One candidate in the staffing report: identity and shortlist facts are deterministic
/// (from the shortlist tool result); the match detail comes from that candidate's match run; the
/// rationale comes from the narrative step, or a deterministic template when it degrades.</summary>
public sealed record StaffingCandidate(
    Guid ExpertId,
    string Name,
    string Title,
    StaffingShortlistDetail Shortlist,
    StaffingMatchDetail Match,
    string Rationale);

/// <summary>The narrative step's validated pick: always one of the report's candidates.</summary>
public sealed record StaffingRecommendation(Guid ExpertId, string Narrative);

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
    ExpertToJob.Agents.Agents.JdRequirements? Extraction = null);

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
    Guid? ExpertId = null,
    StaffingStepStatus? Status = null,
    string? CandidateName = null,
    int? CompletedCount = null,
    int? TotalCount = null,
    string? Error = null);

/// <summary>The pinned <see cref="StaffingProgressEvent.Status"/> values. Closed since EXP-76 —
/// the SSE mapper switches over them, and the SPA's stepper parses these exact strings.</summary>
[JsonConverter(typeof(ClosedStatusJsonConverter<StaffingStepStatus>))]
public closed record StaffingStepStatus : IClosedStatus<StaffingStepStatus>
{
    public sealed record Started : StaffingStepStatus;

    public sealed record Completed : StaffingStepStatus;

    public sealed record Failed : StaffingStepStatus;

    /// <inheritdoc/>
    public string Value => this switch
    {
        Started => "started",
        Completed => "completed",
        Failed => "failed",
    };

    /// <inheritdoc/>
    public static bool TryParse(string? value, [NotNullWhen(true)] out StaffingStepStatus? status)
    {
        status = value switch
        {
            "started" => new Started(),
            "completed" => new Completed(),
            "failed" => new Failed(),
            // The one discard arm the closed set keeps: this switches over a string read back
            // from a column or a payload, not over the hierarchy, so "none of them" is a real
            // case and the caller decides whether it degrades or throws.
            _ => null,
        };
        return status is not null;
    }

    /// <inheritdoc/>
    public static StaffingStepStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new FormatException($"'{value}' is not a step status.");

    public sealed override string ToString() => Value;
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
    IReadOnlyList<StaffingProgressEvent> Events,
    Handoff.HandoffPackage Package);
