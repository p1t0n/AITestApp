using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Persistence.SeedData;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Integration test for <see cref="DemoRosterSeeder"/> (P1T-51) against a real pgvector Postgres
/// (Testcontainers, migrations applied): the full seed → idempotent re-run → wipe → reseed cycle
/// with a trimmed dataset, exercising the DB-level cascades (children + ExpertSearchChunks)
/// the in-memory tests cannot.
/// </summary>
public sealed class DemoRosterSeederPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Seed_wipe_reseed_cycle_is_green_against_real_postgres()
    {
        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync();

            // A non-demo expert that every phase must leave untouched.
            db.Experts.Add(new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace.hopper@example.com",
                Experiences =
                {
                    new Experience
                    {
                        Id = Guid.NewGuid(),
                        Company = "US Navy",
                        Title = "Rear Admiral",
                        StartDate = new DateOnly(1943, 12, 1),
                    },
                },
            });
            await db.SaveChangesAsync();
        }

        var dataset = TrimmedDataset();

        // --- Seed: experts land with children, skills resolved against the upserted catalog ---
        await using (var db = NewDb())
        {
            var seeded = await DemoRosterSeeder.SeedAsync(db, dataset);

            seeded.Should().Be(new DemoRosterSeedResult(Seeded: 2, Skipped: 0));

            var avery = await db.Experts
                .Include(e => e.SpokenLanguages)
                .Include(e => e.AvailabilityEntries)
                .Include(e => e.Skills).ThenInclude(s => s.Skill)
                .Include(e => e.Qualifications)
                .Include(e => e.Experiences).ThenInclude(x => x.Achievements)
                .Include(e => e.Experiences).ThenInclude(x => x.Skills)
                .FirstAsync(e => e.Email == "avery.brightforge@demo.example.com");

            avery.SpokenLanguages.Should().ContainSingle();
            avery.AvailabilityEntries.Should().ContainSingle();
            avery.Qualifications.Should().ContainSingle();
            avery.Skills.Select(s => s.Skill.Name).Should().BeEquivalentTo("C#", "FIX Protocol");
            var experience = avery.Experiences.Should().ContainSingle().Subject;
            experience.Achievements.Should().HaveCount(2);
            experience.Skills.Should().HaveCount(2);

            // Simulate the reconcile worker having indexed a demo expert: the wipe must
            // cascade this chunk away via the DB-level FK.
            db.ExpertSearchChunks.Add(new ExpertSearchChunk
            {
                Id = Guid.NewGuid(),
                ExpertId = avery.Id,
                SourceType = SearchChunkSource.Summary,
                SourceId = avery.Id,
                Content = "chunk",
                ContentHash = "hash",
            });
            await db.SaveChangesAsync();
        }

        // --- Idempotent re-run: nothing new ---
        await using (var db = NewDb())
        {
            var second = await DemoRosterSeeder.SeedAsync(db, dataset);

            second.Should().Be(new DemoRosterSeedResult(Seeded: 0, Skipped: 2));
            (await db.Experts.CountAsync()).Should().Be(3);
        }

        // --- Wipe: exactly the demo-tagged experts go, cascading children and chunks ---
        await using (var db = NewDb())
        {
            var wiped = await DemoRosterSeeder.WipeAsync(db);

            wiped.Should().Be(2);
            (await db.Experts.Select(e => e.Email).ToListAsync())
                .Should().ContainSingle().Which.Should().Be("grace.hopper@example.com");
            (await db.SpokenLanguages.CountAsync()).Should().Be(0);
            (await db.AvailabilityEntries.CountAsync()).Should().Be(0);
            (await db.ExpertSkills.CountAsync()).Should().Be(0);
            (await db.Qualifications.CountAsync()).Should().Be(0);
            (await db.Experiences.CountAsync()).Should().Be(1); // the survivor's
            (await db.Achievements.CountAsync()).Should().Be(0);
            (await db.ExperienceSkills.CountAsync()).Should().Be(0);
            (await db.ExpertSearchChunks.CountAsync()).Should().Be(0);

            // The upserted catalog stays for the next seed.
            (await db.Skills.CountAsync()).Should().Be(2);
        }

        // --- Reseed: cycle starts over cleanly, catalog rows are reused not duplicated ---
        await using (var db = NewDb())
        {
            var reseeded = await DemoRosterSeeder.SeedAsync(db, dataset);

            reseeded.Should().Be(new DemoRosterSeedResult(Seeded: 2, Skipped: 0));
            (await db.Experts.CountAsync()).Should().Be(3);
            (await db.Skills.CountAsync()).Should().Be(2);
            (await db.Categories.CountAsync()).Should().Be(2);
        }
    }

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static DemoRosterDataset TrimmedDataset() => new()
    {
        Skills =
        [
            new DemoRosterSkill { Name = "C#", Category = "Backend / .NET" },
            new DemoRosterSkill { Name = "FIX Protocol", Category = "Fintech / Trading" },
        ],
        Experts =
        [
            DemoExpert("avery.brightforge"),
            DemoExpert("blair.copperfield"),
        ],
    };

    private static DemoRosterExpert DemoExpert(string slug) => new()
    {
        FirstName = slug.Split('.')[0],
        LastName = slug.Split('.')[1],
        Title = "Senior Backend Engineer",
        Email = $"{slug}@demo.example.com",
        Summary = "A decade of low-latency trading systems.",
        Industry = "fintech",
        SpokenLanguages = [new DemoRosterSpokenLanguage { Language = "English", Level = LanguageLevel.Fluent }],
        Availability = [new DemoRosterAvailability { EffectiveFrom = new DateOnly(2026, 3, 1), CapacityPercent = 50 }],
        Skills =
        [
            new DemoRosterExpertSkill { Name = "C#", Level = SkillLevel.Expert, YearsExperience = 9.5m },
            new DemoRosterExpertSkill { Name = "FIX Protocol", Level = SkillLevel.Advanced, YearsExperience = 6m },
        ],
        Qualifications =
        [
            new DemoRosterQualification
            {
                Type = QualificationType.Certification,
                Name = "AWS Certified Solutions Architect",
                Issuer = "Amazon Web Services",
                IssueDate = new DateOnly(2024, 1, 15),
            },
        ],
        Experiences =
        [
            new DemoRosterExperience
            {
                Company = "LedgerPeak Capital",
                Title = "Senior Backend Engineer",
                StartDate = new DateOnly(2021, 2, 1),
                Summary = "Owned the order-routing gateway for equities.",
                Achievements =
                [
                    "Migrated the FIX 4.4 gateway to a zero-allocation pipeline.",
                    "Cut p99 order-ack latency from 3.1 ms to 480 us.",
                ],
                Skills = ["C#", "FIX Protocol"],
            },
        ],
    };
}
