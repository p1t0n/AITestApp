using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// Junction linking an <see cref="Employee"/> to a catalog <see cref="Skill"/>,
/// carrying the employee's proficiency and years of experience with it.
/// </summary>
public class EmployeeSkill
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;

    public SkillLevel Level { get; set; }
    public decimal YearsExperience { get; set; }
}
