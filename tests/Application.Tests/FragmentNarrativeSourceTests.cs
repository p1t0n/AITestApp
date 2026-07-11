using EmployeeManager.Tools.DemoRoster;
using FluentAssertions;
using Xunit;

namespace EmployeeManager.Application.Tests;

/// <summary>
/// Variety guards for the offline narrative fragments: a 500-employee roster written purely
/// from fragments must still read like distinct CVs, not template mush.
/// </summary>
public class FragmentNarrativeSourceTests
{
    private static readonly Lazy<EmployeeManager.Infrastructure.Persistence.SeedData.DemoRosterDataset> FullRoster =
        new(() => DemoRosterGenerator.Generate(new GenerationOptions(), new FragmentNarrativeSource()));

    [Fact]
    public void No_experience_summary_repeats_more_than_three_times()
    {
        var summaries = FullRoster.Value.Employees
            .SelectMany(e => e.Experiences)
            .Select(x => x.Summary)
            .ToList();

        summaries.GroupBy(s => s).Should().AllSatisfy(g => g.Should().HaveCountLessThanOrEqualTo(3));
    }

    [Fact]
    public void Average_experience_summary_length_exceeds_eighty_characters()
    {
        FullRoster.Value.Employees
            .SelectMany(e => e.Experiences)
            .Average(x => (double)x.Summary!.Length)
            .Should().BeGreaterThan(80);
    }

    [Fact]
    public void Achievements_are_varied_and_never_placeholder_mush()
    {
        var achievements = FullRoster.Value.Employees
            .SelectMany(e => e.Experiences)
            .SelectMany(x => x.Achievements)
            .ToList();

        // Looser than the summary guard: ~8,750 bullets drawn from two-slot templates may
        // occasionally coincide, but anything past a handful would mean template mush.
        achievements.GroupBy(a => a).Should().AllSatisfy(g => g.Should().HaveCountLessThanOrEqualTo(5));
        achievements.Should().AllSatisfy(a =>
        {
            a.Length.Should().BeGreaterThan(40);
            a.Should().NotContainAny("{", "}"); // no unfilled template slots
        });
    }

    [Fact]
    public void Employee_summaries_are_present_and_substantial()
    {
        FullRoster.Value.Employees.Should().AllSatisfy(e =>
            e.Summary.Should().NotBeNullOrWhiteSpace().And.Subject.Length.Should().BeGreaterThan(80));
    }

    [Fact]
    public void Roughly_a_tenth_of_the_roster_is_acronym_heavy()
    {
        // Markers that only the acronym/product-name-heavy fragments use.
        string[] markers =
        [
            "FIX 4.4", "PCI-DSS", "ISO 20022", "HL7", "FHIR", "DICOM", "Unity ECS", "HLSL",
            "FreeRTOS", "Zephyr", "CAN 2.0B", "ONNX", "OIDC", "SAML", "WCAG 2.2", "gRPC",
        ];

        var heavyCount = FullRoster.Value.Employees.Count(e =>
            e.Experiences.Any(x =>
                markers.Count(m => (x.Summary + " " + string.Join(" ", x.Achievements)).Contains(m)) >= 2));

        // ~10-15% of 500, with slack for seed noise.
        heavyCount.Should().BeInRange(35, 110);
    }
}
