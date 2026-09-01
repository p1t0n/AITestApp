using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Persistence.SeedData;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Unit tests for <see cref="DemoRosterSeeder"/> (P1T-51) against the in-memory provider, using a
/// trimmed in-code dataset. Relational concerns (real cascades, unique indexes, migrations) are
/// covered by the Testcontainers cycle test in Mcp.Tests.
/// </summary>
public class DemoRosterSeederTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"demo-seeder-{Guid.NewGuid()}")
            .Options);

    // --- Trimmed dataset: 3 experts over a 3-skill catalog spanning 2 categories ---

    private static DemoRosterDataset NewDataset() => new()
    {
        Skills =
        [
            new DemoRosterSkill { Name = "C#", Category = "Backend / .NET" },
            new DemoRosterSkill { Name = "FIX Protocol", Category = "Fintech / Trading" },
            new DemoRosterSkill { Name = "Market Data Feeds", Category = "Fintech / Trading" },
        ],
        Experts =
        [
            DemoExpert("avery.brightforge", "C#", "FIX Protocol"),
            DemoExpert("blair.copperfield", "FIX Protocol", "Market Data Feeds"),
            DemoExpert("casey.duskwalker", "C#", "Market Data Feeds"),
        ],
    };

    private static DemoRosterExpert DemoExpert(string slug, params string[] skillNames) => new()
    {
        FirstName = slug.Split('.')[0],
        LastName = slug.Split('.')[1],
        Title = "Senior Backend Engineer",
        Email = $"{slug}@demo.example.com",
        Phone = "+1 555 0100",
        Location = "Berlin, Germany",
        Summary = "A decade of low-latency trading systems.",
        Industry = "fintech",
        SpokenLanguages = [new DemoRosterSpokenLanguage { Language = "English", Level = LanguageLevel.Fluent }],
        Availability = [new DemoRosterAvailability { EffectiveFrom = new DateOnly(2026, 3, 1), CapacityPercent = 50 }],
        Skills = skillNames
            .Select(n => new DemoRosterExpertSkill { Name = n, Level = SkillLevel.Expert, YearsExperience = 9.5m })
            .ToList(),
        Qualifications =
        [
            new DemoRosterQualification
            {
                Type = QualificationType.Certification,
                Name = "AWS Certified Solutions Architect",
                Issuer = "Amazon Web Services",
                CredentialId = "AWS-123",
                IssueDate = new DateOnly(2024, 1, 15),
                ExpiryDate = new DateOnly(2027, 1, 15),
            },
        ],
        Experiences =
        [
            new DemoRosterExperience
            {
                Company = "LedgerPeak Capital",
                Title = "Senior Backend Engineer",
                Location = "Berlin",
                StartDate = new DateOnly(2021, 2, 1),
                EndDate = null,
                Summary = "Owned the order-routing gateway for equities.",
                Achievements =
                [
                    "Migrated the FIX 4.4 gateway to a zero-allocation pipeline.",
                    "Cut p99 order-ack latency from 3.1 ms to 480 us.",
                ],
                Skills = [.. skillNames],
            },
        ],
    };

    [Fact]
    public async Task Seed_inserts_experts_with_all_children_and_resolves_skills_to_catalog_rows()
    {
        await using var db = NewDb();

        var result = await DemoRosterSeeder.SeedAsync(db, NewDataset());

        result.Should().Be(new DemoRosterSeedResult(Seeded: 3, Skipped: 0));
        (await db.Experts.CountAsync()).Should().Be(3);

        var avery = await db.Experts
            .Include(e => e.SpokenLanguages)
            .Include(e => e.AvailabilityEntries)
            .Include(e => e.Skills).ThenInclude(s => s.Skill)
            .Include(e => e.Qualifications)
            .Include(e => e.Experiences).ThenInclude(x => x.Achievements)
            .Include(e => e.Experiences).ThenInclude(x => x.Skills).ThenInclude(xs => xs.Skill)
            .FirstAsync(e => e.Email == "avery.brightforge@demo.example.com");

        avery.FirstName.Should().Be("avery");
        avery.Summary.Should().NotBeNullOrWhiteSpace();
        avery.SpokenLanguages.Should().ContainSingle().Which.Level.Should().Be(LanguageLevel.Fluent);
        avery.AvailabilityEntries.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { EffectiveFrom = new DateOnly(2026, 3, 1), CapacityPercent = 50 });
        avery.Qualifications.Should().ContainSingle().Which.Issuer.Should().Be("Amazon Web Services");

        // Expert skills resolve to real catalog rows carried by the dataset.
        avery.Skills.Select(s => s.Skill.Name).Should().BeEquivalentTo("C#", "FIX Protocol");
        avery.Skills.Should().AllSatisfy(s => s.YearsExperience.Should().Be(9.5m));

        var experience = avery.Experiences.Should().ContainSingle().Subject;
        experience.Company.Should().Be("LedgerPeak Capital");
        experience.Achievements.Should().HaveCount(2);
        experience.Achievements.OrderBy(a => a.Order).Select(a => a.Order).Should().Equal(1, 2);
        experience.Skills.Select(xs => xs.Skill.Name).Should().BeEquivalentTo("C#", "FIX Protocol");
    }

    [Fact]
    public async Task Seed_upserts_missing_catalog_entries_once_and_leaves_existing_rows_untouched()
    {
        await using var db = NewDb();

        // Base catalog already carries "Backend / .NET" > "C#" — the seeder must reuse both rows.
        var existingCategory = new Category { Id = Guid.NewGuid(), Name = "Backend / .NET" };
        var existingSkill = new Skill { Id = Guid.NewGuid(), Name = "C#", CategoryId = existingCategory.Id };
        db.Categories.Add(existingCategory);
        db.Skills.Add(existingSkill);
        await db.SaveChangesAsync();

        await DemoRosterSeeder.SeedAsync(db, NewDataset());

        (await db.Skills.Where(s => s.Name == "C#").ToListAsync())
            .Should().ContainSingle().Which.Id.Should().Be(existingSkill.Id);
        (await db.Categories.CountAsync(c => c.Name == "Backend / .NET")).Should().Be(1);

        // Two dataset skills share the new category — it must be created exactly once.
        (await db.Categories.CountAsync(c => c.Name == "Fintech / Trading")).Should().Be(1);
        (await db.Skills.CountAsync()).Should().Be(3);

        // Expert skills for "C#" resolve to the pre-existing catalog row, not a duplicate.
        (await db.ExpertSkills.Where(s => s.SkillId == existingSkill.Id).CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Seed_with_count_takes_exactly_the_first_n_experts_deterministically()
    {
        await using var db = NewDb();

        var result = await DemoRosterSeeder.SeedAsync(db, NewDataset(), count: 2);

        result.Seeded.Should().Be(2);
        (await db.Experts.Select(e => e.Email).ToListAsync()).Should().BeEquivalentTo(
            "avery.brightforge@demo.example.com",
            "blair.copperfield@demo.example.com");
    }

    [Fact]
    public async Task Reseeding_without_wipe_adds_zero_new_rows()
    {
        await using var db = NewDb();
        await DemoRosterSeeder.SeedAsync(db, NewDataset());
        var before = await RowCounts(db);

        var second = await DemoRosterSeeder.SeedAsync(db, NewDataset());

        second.Should().Be(new DemoRosterSeedResult(Seeded: 0, Skipped: 3));
        (await RowCounts(db)).Should().Equal(before);
    }

    [Fact]
    public async Task Seeding_a_larger_count_after_a_partial_seed_only_adds_the_missing_experts()
    {
        await using var db = NewDb();
        await DemoRosterSeeder.SeedAsync(db, NewDataset(), count: 1);

        var result = await DemoRosterSeeder.SeedAsync(db, NewDataset(), count: 3);

        result.Should().Be(new DemoRosterSeedResult(Seeded: 2, Skipped: 1));
        (await db.Experts.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Wipe_removes_only_demo_experts_and_cascades_their_children()
    {
        await using var db = NewDb();

        // A non-demo expert (with children) that must survive the wipe untouched.
        var survivor = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace.hopper@example.com",
            SpokenLanguages = { new SpokenLanguage { Id = Guid.NewGuid(), Language = "English", Level = LanguageLevel.Native } },
            Experiences =
            {
                new Experience
                {
                    Id = Guid.NewGuid(),
                    Company = "US Navy",
                    Title = "Rear Admiral",
                    StartDate = new DateOnly(1943, 12, 1),
                    Achievements = { new Achievement { Id = Guid.NewGuid(), Order = 1, Text = "Wrote the first compiler." } },
                },
            },
        };
        db.Experts.Add(survivor);
        await db.SaveChangesAsync();

        await DemoRosterSeeder.SeedAsync(db, NewDataset());
        var wiped = await DemoRosterSeeder.WipeAsync(db);

        wiped.Should().Be(3);
        (await db.Experts.Select(e => e.Email).ToListAsync())
            .Should().ContainSingle().Which.Should().Be("grace.hopper@example.com");

        // Demo children are gone; the survivor's children are intact.
        (await db.SpokenLanguages.CountAsync()).Should().Be(1);
        (await db.AvailabilityEntries.CountAsync()).Should().Be(0);
        (await db.ExpertSkills.CountAsync()).Should().Be(0);
        (await db.Qualifications.CountAsync()).Should().Be(0);
        (await db.Experiences.CountAsync()).Should().Be(1);
        (await db.Achievements.CountAsync()).Should().Be(1);
        (await db.ExperienceSkills.CountAsync()).Should().Be(0);

        // The upserted skill catalog is deliberately left in place.
        (await db.Skills.CountAsync()).Should().Be(3);
    }

    [Fact]
    public void LoadCommittedDataset_returns_the_full_committed_roster()
    {
        var dataset = DemoRosterSeeder.LoadCommittedDataset();

        dataset.Experts.Should().HaveCount(500);
        dataset.Skills.Should().NotBeEmpty();
    }

    private static async Task<int[]> RowCounts(AppDbContext db) =>
    [
        await db.Experts.CountAsync(),
        await db.SpokenLanguages.CountAsync(),
        await db.AvailabilityEntries.CountAsync(),
        await db.ExpertSkills.CountAsync(),
        await db.Qualifications.CountAsync(),
        await db.Experiences.CountAsync(),
        await db.Achievements.CountAsync(),
        await db.ExperienceSkills.CountAsync(),
        await db.Skills.CountAsync(),
        await db.Categories.CountAsync(),
    ];
}
