namespace EmployeeManager.Domain.Entities;

/// <summary>
/// Links a catalog <see cref="Skill"/> to a specific <see cref="Experience"/>, forming an
/// evidence trail ("used React at Acme 2020-22") for later AI matching.
/// </summary>
public class ExperienceSkill
{
    public Guid Id { get; set; }

    public Guid ExperienceId { get; set; }
    public Experience Experience { get; set; } = null!;

    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
}
