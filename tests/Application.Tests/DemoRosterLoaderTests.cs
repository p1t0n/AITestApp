using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence.SeedData;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class DemoRosterLoaderTests
{
    private const string SampleJson = """
        {
          "skills": [
            { "name": "C#", "category": "Backend / .NET" },
            { "name": "FIX Protocol", "category": "Fintech / Trading" }
          ],
          "employees": [
            {
              "firstName": "Avery",
              "lastName": "Brightforge",
              "title": "Senior Backend Engineer",
              "email": "avery.brightforge@demo.example.com",
              "phone": "+1 555 0100",
              "location": "Berlin, Germany",
              "summary": "Backend engineer with a decade of low-latency trading systems.",
              "industry": "fintech",
              "spokenLanguages": [
                { "language": "English", "level": "Fluent" }
              ],
              "availability": [
                { "effectiveFrom": "2026-03-01", "capacityPercent": 50 }
              ],
              "skills": [
                { "name": "C#", "level": "Expert", "yearsExperience": 9.5 }
              ],
              "qualifications": [
                {
                  "type": "Certification",
                  "name": "AWS Certified Solutions Architect",
                  "issuer": "Amazon Web Services",
                  "credentialId": "AWS-123",
                  "issueDate": "2024-01-15",
                  "expiryDate": "2027-01-15"
                }
              ],
              "experiences": [
                {
                  "company": "LedgerPeak Capital",
                  "title": "Senior Backend Engineer",
                  "location": "Berlin",
                  "startDate": "2021-02-01",
                  "endDate": null,
                  "summary": "Owned the order-routing gateway for equities.",
                  "achievements": [
                    "Migrated the FIX 4.4 gateway to a zero-allocation pipeline.",
                    "Cut p99 order-ack latency from 3.1 ms to 480 us."
                  ],
                  "skills": ["C#", "FIX Protocol"]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Load_parses_skills_and_employees_from_json()
    {
        var dataset = DemoRosterLoader.Load(SampleJson);

        dataset.Skills.Should().HaveCount(2);
        dataset.Skills[1].Name.Should().Be("FIX Protocol");
        dataset.Skills[1].Category.Should().Be("Fintech / Trading");

        var employee = dataset.Employees.Should().ContainSingle().Subject;
        employee.FirstName.Should().Be("Avery");
        employee.Email.Should().Be("avery.brightforge@demo.example.com");
        employee.Industry.Should().Be("fintech");
        employee.SpokenLanguages.Should().ContainSingle()
            .Which.Level.Should().Be(LanguageLevel.Fluent);
        employee.Availability.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { EffectiveFrom = new DateOnly(2026, 3, 1), CapacityPercent = 50 });
        employee.Skills.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "C#", Level = SkillLevel.Expert, YearsExperience = 9.5m });
        employee.Qualifications.Should().ContainSingle()
            .Which.Type.Should().Be(QualificationType.Certification);

        var experience = employee.Experiences.Should().ContainSingle().Subject;
        experience.Company.Should().Be("LedgerPeak Capital");
        experience.StartDate.Should().Be(new DateOnly(2021, 2, 1));
        experience.EndDate.Should().BeNull();
        experience.Achievements.Should().HaveCount(2);
        experience.Skills.Should().Contain("FIX Protocol");
    }

    [Fact]
    public void Load_rejects_unknown_json_properties()
    {
        var act = () => DemoRosterLoader.Load("""{ "skills": [], "employees": [], "surprise": 1 }""");

        act.Should().Throw<System.Text.Json.JsonException>();
    }
}
