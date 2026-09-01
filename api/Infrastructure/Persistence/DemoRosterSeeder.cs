using System.Reflection;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence.SeedData;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Infrastructure.Persistence;

/// <summary>Outcome of a demo roster seed pass: how many experts were inserted vs. already present.</summary>
public sealed record DemoRosterSeedResult(int Seeded, int Skipped);

/// <summary>
/// Seeds the committed 500-expert demo roster (P1T-48 dataset) into the database, and wipes it
/// back out. Idempotent by expert email; the dataset's skill catalog is upserted by name so
/// existing catalog rows are reused untouched. Deliberately no embedding logic — the MCP service's
/// reconcile worker picks up new experts on its own.
/// </summary>
public static class DemoRosterSeeder
{
    /// <summary>Every dataset expert's email ends with this; the wipe targets exactly these rows.</summary>
    public const string DemoEmailSuffix = "@demo.example.com";

    /// <summary>Loads the demo-roster.json asset embedded in this assembly via the shared strict loader.</summary>
    public static DemoRosterDataset LoadCommittedDataset()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "ExpertToJob.Infrastructure.Persistence.SeedData.demo-roster.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded demo roster asset '{resourceName}' not found.");
        return DemoRosterLoader.Load(stream);
    }

    /// <summary>
    /// Inserts the first <paramref name="count"/> dataset experts (default: all) with all their
    /// children, skipping experts whose email already exists so re-runs add nothing.
    /// </summary>
    public static async Task<DemoRosterSeedResult> SeedAsync(
        AppDbContext db, DemoRosterDataset dataset, int? count = null, CancellationToken ct = default)
    {
        var wanted = count is { } n ? dataset.Experts.Take(n).ToList() : dataset.Experts;

        var skillsByName = await UpsertCatalogAsync(db, dataset, ct);

        var existingEmails = (await db.Experts.Select(e => e.Email).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seeded = 0;
        var skipped = 0;
        foreach (var source in wanted)
        {
            if (!existingEmails.Add(source.Email))
            {
                skipped++;
                continue;
            }

            db.Experts.Add(BuildExpert(source, skillsByName));
            seeded++;
        }

        await db.SaveChangesAsync(ct);
        return new DemoRosterSeedResult(seeded, skipped);
    }

    /// <summary>
    /// Deletes exactly the experts whose email carries the demo tag; DB cascades remove their
    /// children and ExpertSearchChunks. The upserted skill catalog stays. Returns the wipe count.
    /// </summary>
    public static async Task<int> WipeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var demoExperts = await db.Experts
            .Where(e => e.Email.EndsWith(DemoEmailSuffix))
            .ToListAsync(ct);

        db.Experts.RemoveRange(demoExperts);
        await db.SaveChangesAsync(ct);
        return demoExperts.Count;
    }

    /// <summary>
    /// Adds the dataset's catalog skills (and their categories) that are missing by name; existing
    /// rows are reused as-is. Returns the merged name → skill map expert skills resolve against.
    /// </summary>
    private static async Task<Dictionary<string, Skill>> UpsertCatalogAsync(
        AppDbContext db, DemoRosterDataset dataset, CancellationToken ct)
    {
        var categoriesByName = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in await db.Categories.ToListAsync(ct))
            categoriesByName.TryAdd(category.Name, category);

        var skillsByName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in await db.Skills.ToListAsync(ct))
            skillsByName.TryAdd(skill.Name, skill);

        foreach (var catalogSkill in dataset.Skills)
        {
            if (skillsByName.ContainsKey(catalogSkill.Name))
                continue;

            if (!categoriesByName.TryGetValue(catalogSkill.Category, out var category))
            {
                category = new Category { Id = Guid.NewGuid(), Name = catalogSkill.Category };
                db.Categories.Add(category);
                categoriesByName[catalogSkill.Category] = category;
            }

            var skill = new Skill { Id = Guid.NewGuid(), Name = catalogSkill.Name, CategoryId = category.Id };
            db.Skills.Add(skill);
            skillsByName[catalogSkill.Name] = skill;
        }

        return skillsByName;
    }

    private static Expert BuildExpert(DemoRosterExpert source, Dictionary<string, Skill> skillsByName) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = source.FirstName,
        LastName = source.LastName,
        Title = source.Title,
        Email = source.Email,
        Phone = source.Phone,
        Location = source.Location,
        Summary = source.Summary,
        SpokenLanguages = source.SpokenLanguages
            .Select(l => new SpokenLanguage { Id = Guid.NewGuid(), Language = l.Language, Level = l.Level })
            .ToList(),
        AvailabilityEntries = source.Availability
            .Select(a => new AvailabilityEntry
            {
                Id = Guid.NewGuid(),
                EffectiveFrom = a.EffectiveFrom,
                CapacityPercent = a.CapacityPercent,
            })
            .ToList(),
        Skills = source.Skills
            .Select(s => new ExpertSkill
            {
                Id = Guid.NewGuid(),
                SkillId = Resolve(skillsByName, s.Name, source.Email).Id,
                Level = s.Level,
                YearsExperience = s.YearsExperience,
            })
            .ToList(),
        Qualifications = source.Qualifications
            .Select(q => new Qualification
            {
                Id = Guid.NewGuid(),
                Type = q.Type,
                Name = q.Name,
                Institution = q.Institution,
                Field = q.Field,
                StartDate = q.StartDate,
                EndDate = q.EndDate,
                Issuer = q.Issuer,
                CredentialId = q.CredentialId,
                IssueDate = q.IssueDate,
                ExpiryDate = q.ExpiryDate,
            })
            .ToList(),
        Experiences = source.Experiences
            .Select(x => new Experience
            {
                Id = Guid.NewGuid(),
                Company = x.Company,
                Title = x.Title,
                Location = x.Location,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Summary = x.Summary,
                Achievements = x.Achievements
                    .Select((text, i) => new Achievement { Id = Guid.NewGuid(), Order = i + 1, Text = text })
                    .ToList(),
                Skills = x.Skills
                    .Select(name => new ExperienceSkill
                    {
                        Id = Guid.NewGuid(),
                        SkillId = Resolve(skillsByName, name, source.Email).Id,
                    })
                    .ToList(),
            })
            .ToList(),
    };

    private static Skill Resolve(Dictionary<string, Skill> skillsByName, string name, string email) =>
        skillsByName.TryGetValue(name, out var skill)
            ? skill
            : throw new InvalidOperationException(
                $"Demo expert '{email}' references skill '{name}' missing from both the dataset catalog and the database.");
}
