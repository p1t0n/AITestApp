namespace CvManager.Domain.Entities;

/// <summary>
/// Aggregate root. An available employee whose data feeds CV rendering and (later) AI matching.
/// </summary>
public class Employee
{
    public Guid Id { get; set; }

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

    public ICollection<SpokenLanguage> SpokenLanguages { get; set; } = new List<SpokenLanguage>();
    public ICollection<AvailabilityEntry> AvailabilityEntries { get; set; } = new List<AvailabilityEntry>();
    public ICollection<EmployeeSkill> Skills { get; set; } = new List<EmployeeSkill>();
    public ICollection<Qualification> Qualifications { get; set; } = new List<Qualification>();
    public ICollection<Experience> Experiences { get; set; } = new List<Experience>();
}
