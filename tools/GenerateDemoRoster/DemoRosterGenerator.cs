using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence.SeedData;

namespace ExpertToJob.Tools.DemoRoster;

/// <summary>
/// Deterministically assembles the demo roster: names, emails, industries, careers, skills,
/// qualifications, languages and availability all come from seeded randomness; narrative
/// prose is delegated to an <see cref="INarrativeSource"/>.
/// </summary>
public static class DemoRosterGenerator
{
    /// <summary>Availability step-function shapes; a null start date step means "entry omitted".</summary>
    private static readonly int[][] AvailabilityPatterns =
    [
        [100],
        [0],
        [50],
        [0, 100],
        [0, 50],
        [50, 100],
        [0, 50, 100],
        [100, 50, 0],
        [25, 75, 100],
        [100, 0],
    ];

    public static DemoRosterDataset Generate(GenerationOptions options, INarrativeSource narrativeSource)
    {
        var dataset = new DemoRosterDataset { Skills = DemoSkillCatalog.All.ToList() };
        var catalogNames = dataset.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var profile in IndustryProfiles.All)
        {
            var unknown = profile.SkillPool.Where(s => !catalogNames.Contains(s)).ToList();
            if (unknown.Count > 0)
                throw new InvalidOperationException(
                    $"Industry '{profile.Id}' references skills missing from the catalog: {string.Join(", ", unknown)}");
        }

        var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.EmployeeCount; i++)
        {
            // Round-robin over industries → ten clusters of ~equal size.
            var profile = IndustryProfiles.All[i % IndustryProfiles.All.Count];
            var rng = DeterministicRandom.ForSubStream(options.Seed, i);
            dataset.Employees.Add(BuildEmployee(profile, rng, options, narrativeSource, usedEmails));
        }

        return dataset;
    }

    private static DemoRosterEmployee BuildEmployee(
        IndustryProfile profile,
        DeterministicRandom rng,
        GenerationOptions options,
        INarrativeSource narrativeSource,
        HashSet<string> usedEmails)
    {
        var firstName = rng.Pick(PeoplePools.FirstNames);
        var lastName = rng.Pick(PeoplePools.LastNames);
        var acronymHeavy = rng.Chance(options.AcronymHeavyShare);

        var skills = BuildSkills(profile, rng);
        var skillNames = skills.Select(s => s.Name).ToList();
        var experiences = BuildExperiences(profile, rng, options, narrativeSource, skillNames, acronymHeavy);
        var title = experiences[0].Title;

        return new DemoRosterEmployee
        {
            FirstName = firstName,
            LastName = lastName,
            Title = title,
            Email = UniqueEmail(firstName, lastName, usedEmails),
            Phone = $"+1 555 {rng.Next(0, 9999):D4}",
            Location = rng.Pick(PeoplePools.Locations),
            Industry = profile.Id,
            Summary = narrativeSource.WriteEmployeeSummary(profile.Id, title, skillNames.Take(4).ToList(), rng),
            SpokenLanguages = BuildLanguages(rng),
            Availability = BuildAvailability(rng, options.AnchorDate),
            Skills = skills,
            Qualifications = BuildQualifications(profile, rng),
            Experiences = experiences,
        };
    }

    private static string UniqueEmail(string firstName, string lastName, HashSet<string> usedEmails)
    {
        static string Slug(string name) =>
            new(name.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());

        var baseLocal = $"{Slug(firstName)}.{Slug(lastName)}";
        var email = $"{baseLocal}@demo.example.com";
        for (var n = 2; !usedEmails.Add(email); n++)
            email = $"{baseLocal}{n}@demo.example.com";
        return email;
    }

    private static List<DemoRosterEmployeeSkill> BuildSkills(IndustryProfile profile, DeterministicRandom rng)
    {
        var count = rng.Next(4, 10);

        // Signature skills first so the cluster stays recognizable, then breadth from the pool.
        var picked = profile.SkillPool.Take(2)
            .Concat(rng.Sample(profile.SkillPool.Skip(2).ToList(), count))
            .Distinct()
            .Take(count)
            .ToList();

        return picked.Select(name => new DemoRosterEmployeeSkill
        {
            Name = name,
            Level = (SkillLevel)rng.Next((int)SkillLevel.Beginner, (int)SkillLevel.Expert),
            YearsExperience = rng.Next(2, 24) / 2m, // 1.0 .. 12.0 in half-year steps
        }).ToList();
    }

    private static List<DemoRosterExperience> BuildExperiences(
        IndustryProfile profile,
        DeterministicRandom rng,
        GenerationOptions options,
        INarrativeSource narrativeSource,
        IReadOnlyList<string> skillNames,
        bool acronymHeavy)
    {
        var count = rng.Next(2, 5);
        var companies = rng.Sample(profile.Companies, count);

        // Walk the role ladder so the newest role is the most senior.
        var topRung = rng.Next(count - 1, profile.RoleLadder.Count - 1);

        var experiences = new List<DemoRosterExperience>();
        var currentlyEmployed = rng.Chance(0.7);
        // End of the newest role (or just a cursor when still employed).
        var cursor = currentlyEmployed
            ? options.AnchorDate
            : options.AnchorDate.AddMonths(-rng.Next(1, 6));

        for (var i = 0; i < count; i++)
        {
            var durationMonths = rng.Next(14, 52);
            var start = cursor.AddMonths(-durationMonths);
            var rung = Math.Max(0, topRung - i);
            var roleTitle = profile.RoleLadder[rung];
            var experienceSkills = rng.Sample(skillNames, rng.Next(2, Math.Min(4, skillNames.Count)));

            var narrative = narrativeSource.WriteExperience(
                new NarrativeContext(profile.Id, companies[i], roleTitle, experienceSkills, acronymHeavy), rng);

            experiences.Add(new DemoRosterExperience
            {
                Company = companies[i],
                Title = roleTitle,
                Location = rng.Chance(0.3) ? "Remote" : rng.Pick(PeoplePools.Locations),
                StartDate = start,
                EndDate = i == 0 && currentlyEmployed ? null : cursor,
                Summary = narrative.Summary,
                Achievements = narrative.Achievements.ToList(),
                Skills = experienceSkills,
            });

            // A short gap between roles keeps careers looking human.
            cursor = start.AddMonths(-rng.Next(0, 4));
        }

        return experiences;
    }

    private static List<DemoRosterSpokenLanguage> BuildLanguages(DeterministicRandom rng)
    {
        var languages = new List<DemoRosterSpokenLanguage>
        {
            new()
            {
                Language = "English",
                Level = rng.Pick<LanguageLevel>([LanguageLevel.Professional, LanguageLevel.Fluent, LanguageLevel.Native]),
            },
        };

        foreach (var (language, levels) in rng.Sample(PeoplePools.ExtraLanguages, rng.Next(0, 2)))
            languages.Add(new DemoRosterSpokenLanguage { Language = language, Level = rng.Pick(levels) });

        return languages;
    }

    private static List<DemoRosterAvailability> BuildAvailability(DeterministicRandom rng, DateOnly anchor)
    {
        var pattern = rng.Pick(AvailabilityPatterns);
        var effectiveFrom = anchor.AddDays(rng.Next(-90, 120));

        var entries = new List<DemoRosterAvailability>();
        foreach (var capacity in pattern)
        {
            entries.Add(new DemoRosterAvailability { EffectiveFrom = effectiveFrom, CapacityPercent = capacity });
            effectiveFrom = effectiveFrom.AddDays(rng.Next(30, 180));
        }

        return entries;
    }

    private static List<DemoRosterQualification> BuildQualifications(IndustryProfile profile, DeterministicRandom rng)
    {
        var qualifications = new List<DemoRosterQualification>();

        if (rng.Chance(0.7))
        {
            var (degree, field) = rng.Pick(PeoplePools.Degrees);
            var startYear = 2004 + rng.Next(0, 14);
            qualifications.Add(new DemoRosterQualification
            {
                Type = QualificationType.Degree,
                Name = degree,
                Institution = rng.Pick(PeoplePools.Universities),
                Field = field,
                StartDate = new DateOnly(startYear, 9, 1),
                EndDate = new DateOnly(startYear + (degree.StartsWith("M") ? 2 : 4), 6, 30),
            });
        }

        if (rng.Chance(0.45))
        {
            var issueDate = new DateOnly(2020 + rng.Next(0, 5), rng.Next(1, 12), rng.Next(1, 28));
            qualifications.Add(new DemoRosterQualification
            {
                Type = QualificationType.Certification,
                Name = rng.Pick(profile.Certifications),
                Issuer = "Accredited demo issuer",
                CredentialId = $"DEMO-{rng.Next(10000, 99999)}",
                IssueDate = issueDate,
                ExpiryDate = rng.Chance(0.5) ? issueDate.AddYears(3) : null,
            });
        }

        return qualifications;
    }
}
