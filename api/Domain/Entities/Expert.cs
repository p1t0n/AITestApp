using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// Aggregate root. An available expert whose data feeds CV rendering and (later) AI matching.
/// </summary>
public class Expert
{
    public Guid Id { get; set; }

    /// <summary>
    /// The <see cref="User"/> account this row belongs to — the person the CV is about (P1T-182).
    /// Null means unclaimed: the roster is full of rows nobody has signed up for, and a row a
    /// pending claim has not been approved for is still unclaimed. A unique partial index (where
    /// non-null) makes "one person, one row" database truth rather than service convention.
    ///
    /// <para>Independent of <see cref="User.Role"/>: a Service Manager can be on the bench and own
    /// a row too. Ownership decides which row you reach, not what kind of user you are.</para>
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Draft = agent-staged (resume ingestion), hidden from roster/search/staffing until
    /// a human promotes it. Active = the normal, visible state.</summary>
    public ExpertStatus Status { get; set; } = ExpertStatus.Active;

    /// <summary>
    /// When this person paused themselves, or null while they are on the bench (P1T-185). Hiding is
    /// the Expert's own act and nobody else's: a Service Manager who wants somebody off the bench
    /// deactivates the account instead, so the two mechanisms never blur into "who hid whom".
    ///
    /// <para>A nullable timestamp rather than a third <see cref="ExpertStatus"/> value, and not on
    /// taste: an enum value collides with the <c>Draft → Active</c> promote path ("promote an
    /// inactive draft" means nothing) and silently changes what the partial unique index on
    /// <c>Email</c> enforces, which the claim-matching rule depends on (P1T-184). A hidden Expert
    /// keeps <c>Status = Active</c>, so that index goes on meaning what it means today. The
    /// timestamp also answers "since when", which the transparency view has to disclose.</para>
    /// </summary>
    public DateTimeOffset? HiddenAt { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Professional headline, e.g. "Senior Backend Engineer".</summary>
    public string Title { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Location { get; set; }

    /// <summary>Free-text professional summary / bio shown at the top of the CV.</summary>
    public string? Summary { get; set; }

    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Why we are allowed to hold this row, and how that changed over time (P1T-183). Append-only,
    /// and never empty: a roster row with no recorded basis is a compliance defect, so every path
    /// that creates an Expert writes the first record in the same transaction.
    /// </summary>
    public ICollection<ProcessingRecord> ProcessingRecords { get; set; } = new List<ProcessingRecord>();

    public ICollection<SpokenLanguage> SpokenLanguages { get; set; } = new List<SpokenLanguage>();
    public ICollection<AvailabilityEntry> AvailabilityEntries { get; set; } = new List<AvailabilityEntry>();
    public ICollection<ExpertSkill> Skills { get; set; } = new List<ExpertSkill>();
    public ICollection<Qualification> Qualifications { get; set; } = new List<Qualification>();
    public ICollection<Experience> Experiences { get; set; } = new List<Experience>();
}
