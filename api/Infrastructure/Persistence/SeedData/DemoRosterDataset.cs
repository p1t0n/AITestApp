using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Infrastructure.Persistence.SeedData;

/// <summary>
/// Shape of the committed <c>demo-roster.json</c> asset (P1T-48): a self-contained synthetic
/// roster for demoing semantic search. Carries its own skill catalog so a later seeder slice
/// can upsert skills the base <see cref="DbInitializer"/> catalog is missing. All employee
/// emails end in <c>@demo.example.com</c> — the wipe-tag a later slice uses to remove demo data.
/// </summary>
public sealed class DemoRosterDataset
{
    public List<DemoRosterSkill> Skills { get; set; } = [];
    public List<DemoRosterEmployee> Employees { get; set; } = [];
}

/// <summary>Catalog skill carried by the dataset; <see cref="Category"/> is the category name.</summary>
public sealed class DemoRosterSkill
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class DemoRosterEmployee
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }

    /// <summary>Industry cluster tag, e.g. "fintech" or "gaming" (dataset metadata, not persisted).</summary>
    public string Industry { get; set; } = string.Empty;

    public List<DemoRosterSpokenLanguage> SpokenLanguages { get; set; } = [];
    public List<DemoRosterAvailability> Availability { get; set; } = [];
    public List<DemoRosterEmployeeSkill> Skills { get; set; } = [];
    public List<DemoRosterQualification> Qualifications { get; set; } = [];
    public List<DemoRosterExperience> Experiences { get; set; } = [];
}

public sealed class DemoRosterSpokenLanguage
{
    public string Language { get; set; } = string.Empty;
    public LanguageLevel Level { get; set; }
}

public sealed class DemoRosterAvailability
{
    public DateOnly EffectiveFrom { get; set; }
    public int CapacityPercent { get; set; }
}

/// <summary>References a catalog skill from the dataset's <see cref="DemoRosterDataset.Skills"/> by name.</summary>
public sealed class DemoRosterEmployeeSkill
{
    public string Name { get; set; } = string.Empty;
    public SkillLevel Level { get; set; }
    public decimal YearsExperience { get; set; }
}

/// <summary>Mirrors <see cref="Domain.Entities.Qualification"/>'s sparse-column shape.</summary>
public sealed class DemoRosterQualification
{
    public QualificationType Type { get; set; }
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

public sealed class DemoRosterExperience
{
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Summary { get; set; }
    public List<string> Achievements { get; set; } = [];

    /// <summary>Skill names used in this role; each must resolve against the dataset's skill catalog.</summary>
    public List<string> Skills { get; set; } = [];
}
