namespace CvManager.Tools.DemoRoster;

public sealed record GenerationOptions
{
    public int EmployeeCount { get; init; } = 500;

    /// <summary>Roster seed; the committed dataset was produced with the default.</summary>
    public int Seed { get; init; } = 48;

    /// <summary>
    /// Fixed "today" used for career and availability dates, so regeneration with the same
    /// seed yields the same file no matter when it runs.
    /// </summary>
    public DateOnly AnchorDate { get; init; } = new(2026, 7, 1);

    /// <summary>Share of employees whose narratives are written acronym/product-name-heavy.</summary>
    public double AcronymHeavyShare { get; init; } = 0.13;
}
