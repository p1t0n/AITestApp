using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class CvServiceTests
{
    private static ExpertDetailDto SampleExpert() => new(
        Id: Guid.NewGuid(),
        FirstName: "Alice",
        LastName: "Nguyen",
        Title: "Senior Backend Engineer",
        Email: "alice@example.com",
        Phone: null,
        Location: "Berlin",
        Summary: "Backend engineer.",
        PhotoUrl: null,
        CurrentCapacityPercent: 50,
        Status: ExpertStatus.Active,
        SpokenLanguages: new[] { new SpokenLanguageDto(Guid.NewGuid(), "English", LanguageLevel.Fluent) },
        AvailabilityEntries: new[] { new AvailabilityEntryDto(Guid.NewGuid(), new DateOnly(2027, 4, 1), 50) },
        Skills: new[]
        {
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "C#", "Backend", SkillLevel.Expert, 9),
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "EF Core", "Backend", SkillLevel.Advanced, 7),
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "PostgreSQL", "Data", SkillLevel.Advanced, 6),
        },
        Qualifications: new[]
        {
            new QualificationDto(Guid.NewGuid(), QualificationType.Degree, "MSc CS", "TU Munich", "Systems",
                new DateOnly(2013, 9, 1), new DateOnly(2015, 6, 30), null, null, null, null),
            new QualificationDto(Guid.NewGuid(), QualificationType.Certification, "AWS SAA", null, null,
                null, null, "AWS", "ID-1", new DateOnly(2023, 3, 12), new DateOnly(2026, 3, 12)),
        },
        Experiences: new[]
        {
            new ExperienceDto(Guid.NewGuid(), "Acme", "Senior Engineer", "Berlin",
                new DateOnly(2020, 1, 1), null, "Lead backend.",
                new[] { new AchievementDto(Guid.NewGuid(), 1, "Cut latency 40%.") },
                new[] { new ExperienceSkillDto(Guid.NewGuid(), Guid.NewGuid(), "C#") }),
        });

    [Fact]
    public void Groups_skills_by_category()
    {
        var cv = CvService.Build(SampleExpert());

        cv.SkillGroups.Should().HaveCount(2);
        cv.SkillGroups.Should().ContainSingle(g => g.Category == "Backend").Which.Skills.Should().HaveCount(2);
        cv.SkillGroups.Should().ContainSingle(g => g.Category == "Data");
    }

    [Fact]
    public void Splits_education_and_certifications()
    {
        var cv = CvService.Build(SampleExpert());

        cv.Education.Should().ContainSingle(q => q.Name == "MSc CS");
        cv.Certifications.Should().ContainSingle(q => q.Name == "AWS SAA");
    }

    [Fact]
    public void Formats_current_role_period_as_present()
    {
        var cv = CvService.Build(SampleExpert());

        cv.Experiences.Should().ContainSingle().Which.Period.Should().Be("Jan 2020 – Present");
    }

    [Fact]
    public void Carries_experience_and_achievement_ids_for_downstream_tools()
    {
        // style_exemplar_search is keyed by achievement id, and the tailoring agent joins
        // rewrites back onto experiences — the CV projection must expose both ids.
        var expert = SampleExpert();

        var cv = CvService.Build(expert);

        var experience = cv.Experiences.Should().ContainSingle().Subject;
        experience.Id.Should().Be(expert.Experiences[0].Id);
        var achievement = experience.Achievements.Should().ContainSingle().Subject;
        achievement.Id.Should().Be(expert.Experiences[0].Achievements[0].Id);
        achievement.Text.Should().Be("Cut latency 40%.");
    }

    [Fact]
    public void Builds_full_name_and_carries_availability()
    {
        var cv = CvService.Build(SampleExpert());

        cv.FullName.Should().Be("Alice Nguyen");
        cv.Availability.CurrentCapacityPercent.Should().Be(50);
    }
}
