namespace EmployeeManager.Domain.Entities;

/// <summary>
/// A skill in the shared catalog. Employees link to it via <see cref="EmployeeSkill"/>.
/// </summary>
public class Skill
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public ICollection<EmployeeSkill> EmployeeSkills { get; set; } = new List<EmployeeSkill>();
}
