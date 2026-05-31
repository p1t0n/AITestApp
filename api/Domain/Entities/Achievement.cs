namespace EmployeeManager.Domain.Entities;

/// <summary>
/// An ordered bullet point under a work <see cref="Experience"/>, rendered directly on the CV.
/// </summary>
public class Achievement
{
    public Guid Id { get; set; }

    public Guid ExperienceId { get; set; }
    public Experience Experience { get; set; } = null!;

    /// <summary>Display order within the experience (ascending).</summary>
    public int Order { get; set; }

    public string Text { get; set; } = string.Empty;
}
