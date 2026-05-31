namespace EmployeeManager.Domain.Entities;

/// <summary>
/// Self-referencing skill category tree, e.g. Languages &gt; JavaScript &gt; React.
/// </summary>
public class Category
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Skill> Skills { get; set; } = new List<Skill>();
}
