using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// Junction linking an <see cref="Expert"/> to a catalog <see cref="Skill"/>,
/// carrying the expert's proficiency and years of experience with it.
/// </summary>
public class ExpertSkill
{
    public Guid Id { get; set; }

    public Guid ExpertId { get; set; }
    public Expert Expert { get; set; } = null!;

    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;

    public SkillLevel Level { get; set; }
    public decimal YearsExperience { get; set; }
}
