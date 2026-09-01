namespace ExpertToJob.Domain.Entities;

/// <summary>
/// A work-experience record. <see cref="EndDate"/> null means current role.
/// </summary>
public class Experience
{
    public Guid Id { get; set; }

    public Guid ExpertId { get; set; }
    public Expert Expert { get; set; } = null!;

    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public string? Summary { get; set; }

    public ICollection<Achievement> Achievements { get; set; } = new List<Achievement>();
    public ICollection<ExperienceSkill> Skills { get; set; } = new List<ExperienceSkill>();
}
