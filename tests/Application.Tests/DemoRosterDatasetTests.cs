using ExpertToJob.Infrastructure.Persistence.SeedData;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Validates the committed demo-roster.json asset itself (P1T-48), independently of how it
/// was generated: if the file is ever regenerated or hand-edited, these are the invariants
/// the seeder slice and the semantic-search demo rely on.
/// </summary>
public class DemoRosterDatasetTests
{
    private static readonly Lazy<DemoRosterDataset> Dataset = new(LoadCommittedDataset);

    private static DemoRosterDataset LoadCommittedDataset()
    {
        // Walk up from the test output directory to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run from within the repository");

        var path = Path.Combine(dir!.FullName, "api", "Infrastructure", "Persistence", "SeedData", "demo-roster.json");
        File.Exists(path).Should().BeTrue($"the committed dataset asset must exist at {path}");

        using var stream = File.OpenRead(path);
        return DemoRosterLoader.Load(stream);
    }

    [Fact]
    public void Contains_exactly_five_hundred_employees()
    {
        Dataset.Value.Employees.Should().HaveCount(500);
    }

    [Fact]
    public void Every_email_is_unique_and_carries_the_demo_wipe_tag_domain()
    {
        var emails = Dataset.Value.Employees.Select(e => e.Email).ToList();

        emails.Should().OnlyHaveUniqueItems();
        emails.Should().AllSatisfy(e => e.Should().EndWith("@demo.example.com"));
    }

    [Fact]
    public void Every_employee_has_a_complete_valid_profile()
    {
        var catalog = Dataset.Value.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        Dataset.Value.Employees.Should().AllSatisfy(e =>
        {
            e.FirstName.Should().NotBeNullOrWhiteSpace();
            e.LastName.Should().NotBeNullOrWhiteSpace();
            e.Title.Should().NotBeNullOrWhiteSpace();

            e.Experiences.Should().HaveCountGreaterThanOrEqualTo(2).And.HaveCountLessThanOrEqualTo(5);
            e.Experiences.Should().AllSatisfy(x =>
            {
                x.Summary.Should().NotBeNullOrWhiteSpace();
                x.Achievements.Should().HaveCountGreaterThanOrEqualTo(2).And.HaveCountLessThanOrEqualTo(5);
                x.Skills.Should().AllSatisfy(n => catalog.Should().Contain(n));
            });

            e.Skills.Should().HaveCountGreaterThanOrEqualTo(4).And.HaveCountLessThanOrEqualTo(10);
            e.Skills.Should().AllSatisfy(s => catalog.Should().Contain(s.Name));

            e.SpokenLanguages.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(3);
            e.Qualifications.Should().HaveCountLessThanOrEqualTo(2);

            e.Availability.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(3);
            e.Availability.Should().AllSatisfy(a => a.CapacityPercent.Should().BeInRange(0, 100));
        });
    }

    [Fact]
    public void Skill_catalog_is_extended_beyond_the_base_seed()
    {
        Dataset.Value.Skills.Should().HaveCountGreaterThanOrEqualTo(60).And.HaveCountLessThanOrEqualTo(80);
        Dataset.Value.Skills.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        Dataset.Value.Skills.Should().AllSatisfy(s => s.Category.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Narratives_are_varied_no_summary_repeated_more_than_three_times()
    {
        var summaries = Dataset.Value.Employees
            .SelectMany(e => e.Experiences)
            .Select(x => x.Summary)
            .ToList();

        summaries.GroupBy(s => s).Should().AllSatisfy(g => g.Should().HaveCountLessThanOrEqualTo(3));
        summaries.Average(s => (double)s!.Length).Should().BeGreaterThan(80);
    }

    [Fact]
    public void Ten_industry_clusters_are_present()
    {
        Dataset.Value.Employees.Select(e => e.Industry).Distinct().Should().HaveCount(10);
    }
}
