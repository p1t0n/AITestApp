using ExpertToJob.Infrastructure.Persistence.SeedData;
using ExpertToJob.Tools.DemoRoster;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Deterministic-assembly tests for the demo roster generator (tools/GenerateDemoRoster).
/// Uses a stub narrative source so the structural invariants are checked independently of
/// the narrative fragments / LLM enrichment.
/// </summary>
public class DemoRosterGeneratorTests
{
    /// <summary>Fake text source: fixed, cheap narratives so tests exercise assembly only.</summary>
    private sealed class StubNarrativeSource : INarrativeSource
    {
        public string WriteExpertSummary(string industry, string title, IReadOnlyList<string> topSkills, DeterministicRandom rng) =>
            $"Stub professional summary for a {title} in {industry}.";

        public ExperienceNarrative WriteExperience(NarrativeContext context, DeterministicRandom rng) =>
            new($"Stub role narrative at {context.Company}.",
                ["Stub achievement one.", "Stub achievement two."]);
    }

    private static DemoRosterDataset Generate(int count = 100, int seed = 48) =>
        DemoRosterGenerator.Generate(new GenerationOptions { ExpertCount = count, Seed = seed }, new StubNarrativeSource());

    [Fact]
    public void Generates_the_requested_number_of_experts()
    {
        Generate(count: 137).Experts.Should().HaveCount(137);
    }

    [Fact]
    public void Every_email_is_unique_and_ends_with_the_demo_wipe_tag_domain()
    {
        var emails = Generate(count: 500).Experts.Select(e => e.Email).ToList();

        emails.Should().OnlyHaveUniqueItems();
        emails.Should().AllSatisfy(e => e.Should().EndWith("@demo.example.com"));
    }

    [Fact]
    public void Every_expert_has_two_to_five_experiences_with_narratives_from_the_source()
    {
        var experts = Generate().Experts;

        experts.Should().AllSatisfy(e =>
        {
            e.Experiences.Should().HaveCountGreaterThanOrEqualTo(2).And.HaveCountLessThanOrEqualTo(5);
            e.Experiences.Should().AllSatisfy(x =>
            {
                x.Summary.Should().StartWith("Stub role narrative at ");
                x.Achievements.Should().HaveCount(2);
            });
        });
    }

    [Fact]
    public void Experience_dates_are_a_plausible_career_walking_back_in_time()
    {
        var experts = Generate().Experts;

        experts.Should().AllSatisfy(e =>
        {
            // Newest first; each earlier role ends before (or when) the next one starts.
            var ordered = e.Experiences;
            for (var i = 0; i < ordered.Count; i++)
            {
                var exp = ordered[i];
                if (i == 0)
                    exp.EndDate?.Should().BeAfter(exp.StartDate);
                else
                {
                    exp.EndDate.Should().NotBeNull();
                    exp.EndDate!.Value.Should().BeAfter(exp.StartDate);
                    exp.EndDate.Value.Should().BeOnOrBefore(ordered[i - 1].StartDate);
                }
            }
        });
    }

    [Fact]
    public void Every_expert_has_four_to_ten_skills_all_resolvable_against_the_dataset_catalog()
    {
        var dataset = Generate();
        var catalog = dataset.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        dataset.Experts.Should().AllSatisfy(e =>
        {
            e.Skills.Should().HaveCountGreaterThanOrEqualTo(4).And.HaveCountLessThanOrEqualTo(10);
            e.Skills.Select(s => s.Name).Should().OnlyHaveUniqueItems();
            e.Skills.Should().AllSatisfy(s => catalog.Should().Contain(s.Name));
            e.Experiences.SelectMany(x => x.Skills).Should().AllSatisfy(n => catalog.Should().Contain(n));
        });
    }

    [Fact]
    public void Dataset_carries_an_extended_skill_catalog_with_categories()
    {
        var dataset = Generate(count: 10);

        dataset.Skills.Should().HaveCountGreaterThanOrEqualTo(60).And.HaveCountLessThanOrEqualTo(80);
        dataset.Skills.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        dataset.Skills.Should().AllSatisfy(s => s.Category.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Every_expert_has_valid_availability_languages_and_qualifications()
    {
        var experts = Generate().Experts;

        experts.Should().AllSatisfy(e =>
        {
            e.Availability.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(3);
            e.Availability.Should().AllSatisfy(a => a.CapacityPercent.Should().BeInRange(0, 100));
            e.Availability.Select(a => a.EffectiveFrom).Should().BeInAscendingOrder()
                .And.OnlyHaveUniqueItems();

            e.SpokenLanguages.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(3);
            e.SpokenLanguages.Select(l => l.Language).Should().OnlyHaveUniqueItems();

            e.Qualifications.Should().HaveCountLessThanOrEqualTo(2);
        });
    }

    [Fact]
    public void Availability_step_functions_are_varied_across_the_roster()
    {
        var experts = Generate(count: 500).Experts;

        // The roster must mix 0/50/100 step-functions, not hand everyone the same entry.
        var capacities = experts.SelectMany(e => e.Availability).Select(a => a.CapacityPercent).Distinct();
        capacities.Should().Contain([0, 50, 100]);
        experts.Select(e => e.Availability.Count).Distinct().Should().Contain([1, 2, 3]);
    }

    [Fact]
    public void Experts_spread_across_ten_industry_clusters()
    {
        var experts = Generate(count: 500).Experts;

        var byIndustry = experts.GroupBy(e => e.Industry).ToList();
        byIndustry.Should().HaveCount(10);
        byIndustry.Should().AllSatisfy(g => g.Should().HaveCountGreaterThanOrEqualTo(30));
    }

    [Fact]
    public void Generation_is_deterministic_for_a_given_seed()
    {
        var first = DemoRosterLoader.Serialize(Generate(count: 50, seed: 7));
        var second = DemoRosterLoader.Serialize(Generate(count: 50, seed: 7));
        var other = DemoRosterLoader.Serialize(Generate(count: 50, seed: 8));

        second.Should().Be(first);
        other.Should().NotBe(first);
    }
}
