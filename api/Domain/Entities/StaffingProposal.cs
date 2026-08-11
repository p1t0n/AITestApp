namespace CvManager.Domain.Entities;

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
    public Guid? RecommendedEmployeeId { get; set; }

    /// <summary>Whether the source report shipped degraded — reviewers should weigh partial
    /// evidence accordingly.</summary>
    public bool ReportDegraded { get; set; }

    /// <summary>One of <see cref="StaffingProposalStatus"/>.</summary>
    public string Status { get; set; } = StaffingProposalStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    public List<StaffingProposalCandidate> Candidates { get; set; } = [];
}

/// <summary>One candidate snapshot inside a proposal. Identity and scores are deterministic
/// (lifted from captured tool results via the report); the rationale is model narrative kept as
/// display text only.</summary>
public class StaffingProposalCandidate
{
    public Guid Id { get; set; }

    public Guid ProposalId { get; set; }

    public Guid EmployeeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>1-based position in the report's candidate order.</summary>
    public int Rank { get; set; }

    public int? MatchScore { get; set; }

    public string? MatchBand { get; set; }

    public string Rationale { get; set; } = string.Empty;
}

/// <summary>The pinned proposal statuses. Only a human decision moves a proposal off Pending.</summary>
public static class StaffingProposalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
