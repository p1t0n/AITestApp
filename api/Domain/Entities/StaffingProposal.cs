using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ExpertToJob.Domain.Status;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// A staffing run's outcome held for human decision (P1T-100). The pipeline only ever proposes;
/// approving or rejecting is a human act recorded here — the agent layer never gets write
/// authority over staffing outcomes. This record is the decision ledger, not a booking: no
/// downstream write happens on approval yet (a follow-up owns turning approvals into
/// assignments once that domain concept exists).
/// </summary>
public class StaffingProposal
{
    public Guid Id { get; set; }

    /// <summary>Who ran the staffing pipeline. Null when the run was unattributed.</summary>
    public Guid? RequestedByUserId { get; set; }

    public string JobDescription { get; set; } = string.Empty;

    /// <summary>The narrative step's validated pick, when the run produced one.</summary>
    public Guid? RecommendedExpertId { get; set; }

    /// <summary>Whether the source report shipped degraded — reviewers should weigh partial
    /// evidence accordingly.</summary>
    public bool ReportDegraded { get; set; }

    public StaffingProposalStatus Status { get; set; } = new StaffingProposalStatus.Pending();

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>The serialized handoff package (P1T-133): inputs, the full report, provenance,
    /// stage slices, and degradations — everything the approver needs to decide without
    /// re-running the pipeline. Jsonb on PostgreSQL. Null on rows created before the package
    /// existed; the snapshot columns above remain the queryable index either way.</summary>
    public string? PackageJson { get; set; }

    public List<StaffingProposalCandidate> Candidates { get; set; } = [];
}

/// <summary>One candidate snapshot inside a proposal. Identity and scores are deterministic
/// (lifted from captured tool results via the report); the rationale is model narrative kept as
/// display text only.</summary>
public class StaffingProposalCandidate
{
    public Guid Id { get; set; }

    public Guid ProposalId { get; set; }

    public Guid ExpertId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>1-based position in the report's candidate order.</summary>
    public int Rank { get; set; }

    public int? MatchScore { get; set; }

    public string? MatchBand { get; set; }

    public string Rationale { get; set; } = string.Empty;
}

/// <summary>The pinned proposal statuses. Only a human decision moves a proposal off Pending.
///
/// <para>Closed (EXP-76). The stored spelling is unchanged — the approval inbox indexes on this
/// column and the rows in it predate the type.</para></summary>
[JsonConverter(typeof(ClosedStatusJsonConverter<StaffingProposalStatus>))]
public closed record StaffingProposalStatus : IClosedStatus<StaffingProposalStatus>
{
    public sealed record Pending : StaffingProposalStatus;

    public sealed record Approved : StaffingProposalStatus;

    public sealed record Rejected : StaffingProposalStatus;

    /// <inheritdoc/>
    public string Value => this switch
    {
        Pending => "pending",
        Approved => "approved",
        Rejected => "rejected",
    };

    /// <inheritdoc/>
    public static bool TryParse(string? value, [NotNullWhen(true)] out StaffingProposalStatus? status)
    {
        status = value switch
        {
            "pending" => new Pending(),
            "approved" => new Approved(),
            "rejected" => new Rejected(),
            // The one discard arm the closed set keeps: this switches over a string read back
            // from a column or a payload, not over the hierarchy, so "none of them" is a real
            // case and the caller decides whether it degrades or throws.
            _ => null,
        };
        return status is not null;
    }

    /// <inheritdoc/>
    public static StaffingProposalStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new FormatException($"'{value}' is not a proposal status.");

    public sealed override string ToString() => Value;
}
