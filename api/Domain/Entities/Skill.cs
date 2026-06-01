namespace EmployeeManager.Domain.Entities;

/// <summary>
/// A skill in the shared catalog. Employees link to it via <see cref="EmployeeSkill"/>.
/// </summary>
public class Skill
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Computed ranking score (higher = more prominent). Populated by a later ranking
    /// calculation; defaults to 0. Skills are listed by Rank descending, then Name.
    /// </summary>
    public int Rank { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public ICollection<EmployeeSkill> EmployeeSkills { get; set; } = new List<EmployeeSkill>();
}
