using EmployeeManager.Domain.Enums;

namespace EmployeeManager.Domain.Entities;

public class SpokenLanguage
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public string Language { get; set; } = string.Empty;
    public LanguageLevel Level { get; set; }
}
