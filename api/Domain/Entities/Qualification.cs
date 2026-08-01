using CvManager.Domain.Enums;

namespace CvManager.Domain.Entities;

/// <summary>
/// Education or certification. A single entity discriminated by <see cref="Type"/>;
/// fields are nullable and only some apply per type (accepted sparse-column trade-off).
/// Degree:        Institution, Field, StartDate, EndDate.
/// Certification: Issuer, CredentialId, IssueDate, ExpiryDate.
/// </summary>
public class Qualification
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public QualificationType Type { get; set; }

    /// <summary>Degree name or certification name, e.g. "BSc Computer Science" / "AWS Solutions Architect".</summary>
    public string Name { get; set; } = string.Empty;

    // Degree-oriented
    public string? Institution { get; set; }
    public string? Field { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    // Certification-oriented
    public string? Issuer { get; set; }
    public string? CredentialId { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}
