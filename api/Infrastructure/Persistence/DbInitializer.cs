using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Infrastructure.Persistence;

/// <summary>
/// Idempotent dev seeder: a skill-category tree + catalog skills, plus a few fully-populated
/// sample employees so the API, CV view, and (later) AI tooling have realistic data to work with.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(ct))
            return; // already seeded

        // --- Skill category tree ---
        Category Cat(string name, Category? parent = null) =>
            new() { Id = Guid.NewGuid(), Name = name, ParentId = parent?.Id, Parent = parent };

        var languages = Cat("Languages");
        var js = Cat("JavaScript", languages);
        var frontend = Cat("Frontend", js);
        var dotnet = Cat("Backend / .NET");
        var data = Cat("Data");
        var cloud = Cat("Cloud & DevOps");
        var practices = Cat("Practices");

        var categories = new[] { languages, js, frontend, dotnet, data, cloud, practices };
        db.Categories.AddRange(categories);

        // --- Catalog skills ---
        var skills = new Dictionary<string, Skill>();
        Skill Sk(string name, Category category)
        {
            var s = new Skill { Id = Guid.NewGuid(), Name = name, CategoryId = category.Id };
            skills[name] = s;
            return s;
        }

        db.Skills.AddRange(
            Sk("JavaScript", js),
            Sk("TypeScript", js),
            Sk("React", frontend),
            Sk("MUI", frontend),
            Sk("C#", dotnet),
            Sk("ASP.NET Core", dotnet),
            Sk("Entity Framework Core", dotnet),
            Sk("PostgreSQL", data),
            Sk("SQL", data),
            Sk("Redis", data),
            Sk("Docker", cloud),
            Sk("AWS", cloud),
            Sk("Azure", cloud),
            Sk("CI/CD", cloud),
            Sk("Agile / Scrum", practices),
            Sk("Test-Driven Development", practices)
        );

        // --- Sample employees ---
        db.Employees.AddRange(
            BuildAlice(skills),
            BuildBob(skills),
            BuildCarol(skills)
        );

        await db.SaveChangesAsync(ct);
    }

    private static EmployeeSkill ES(Skill skill, SkillLevel level, decimal years) =>
        new() { Id = Guid.NewGuid(), SkillId = skill.Id, Level = level, YearsExperience = years };

    private static ExperienceSkill XS(Skill skill) =>
        new() { Id = Guid.NewGuid(), SkillId = skill.Id };

    private static Achievement Ach(int order, string text) =>
        new() { Id = Guid.NewGuid(), Order = order, Text = text };

    private static Employee BuildAlice(Dictionary<string, Skill> s) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Alice",
        LastName = "Nguyen",
        Title = "Senior Backend Engineer",
        Email = "alice.nguyen@example.com",
        Phone = "+1 555 0101",
        Location = "Berlin, Germany",
        Summary = "Backend engineer with 9 years building .NET services and data-heavy platforms. " +
                  "Strong on EF Core, PostgreSQL and clean architecture.",
        SpokenLanguages =
        {
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "English", Level = LanguageLevel.Fluent },
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "German", Level = LanguageLevel.Professional },
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "Vietnamese", Level = LanguageLevel.Native },
        },
        // Step-function availability matching the SPEC example.
        AvailabilityEntries =
        {
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2027, 4, 1), CapacityPercent = 50 },
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2027, 7, 1), CapacityPercent = 75 },
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2027, 11, 1), CapacityPercent = 100 },
        },
        Skills =
        {
            ES(s["C#"], SkillLevel.Expert, 9),
            ES(s["ASP.NET Core"], SkillLevel.Expert, 8),
            ES(s["Entity Framework Core"], SkillLevel.Expert, 7),
            ES(s["PostgreSQL"], SkillLevel.Advanced, 6),
            ES(s["Docker"], SkillLevel.Advanced, 5),
            ES(s["AWS"], SkillLevel.Intermediate, 3),
        },
        Qualifications =
        {
            new Qualification
            {
                Id = Guid.NewGuid(), Type = QualificationType.Degree,
                Name = "MSc Computer Science", Institution = "TU Munich", Field = "Distributed Systems",
                StartDate = new DateOnly(2013, 9, 1), EndDate = new DateOnly(2015, 6, 30),
            },
            new Qualification
            {
                Id = Guid.NewGuid(), Type = QualificationType.Certification,
                Name = "AWS Certified Solutions Architect – Associate", Issuer = "Amazon Web Services",
                CredentialId = "AWS-SAA-2023-1187", IssueDate = new DateOnly(2023, 3, 12), ExpiryDate = new DateOnly(2026, 3, 12),
            },
        },
        Experiences =
        {
            new Experience
            {
                Id = Guid.NewGuid(), Company = "Acme Logistics", Title = "Senior Backend Engineer",
                Location = "Berlin", StartDate = new DateOnly(2020, 1, 1), EndDate = null,
                Summary = "Lead backend for the shipment-tracking platform.",
                Achievements =
                {
                    Ach(1, "Cut p95 API latency 40% by redesigning the EF Core query layer."),
                    Ach(2, "Migrated a 2 TB monolith database to partitioned PostgreSQL with zero downtime."),
                },
                Skills = { XS(s["C#"]), XS(s["ASP.NET Core"]), XS(s["Entity Framework Core"]), XS(s["PostgreSQL"]) },
            },
            new Experience
            {
                Id = Guid.NewGuid(), Company = "DataForge GmbH", Title = "Backend Engineer",
                Location = "Munich", StartDate = new DateOnly(2015, 7, 1), EndDate = new DateOnly(2019, 12, 31),
                Summary = "Built ingestion pipelines and internal APIs.",
                Achievements =
                {
                    Ach(1, "Designed the event-ingestion service handling 50M events/day."),
                },
                Skills = { XS(s["C#"]), XS(s["Docker"]) },
            },
        },
    };

    private static Employee BuildBob(Dictionary<string, Skill> s) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Bob",
        LastName = "Schmidt",
        Title = "Frontend Engineer",
        Email = "bob.schmidt@example.com",
        Phone = "+1 555 0102",
        Location = "Remote (EU)",
        Summary = "Frontend engineer focused on React, TypeScript and design systems.",
        SpokenLanguages =
        {
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "English", Level = LanguageLevel.Professional },
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "German", Level = LanguageLevel.Native },
        },
        AvailabilityEntries =
        {
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2026, 1, 1), CapacityPercent = 100 },
        },
        Skills =
        {
            ES(s["React"], SkillLevel.Expert, 6),
            ES(s["TypeScript"], SkillLevel.Advanced, 5),
            ES(s["JavaScript"], SkillLevel.Expert, 7),
            ES(s["MUI"], SkillLevel.Advanced, 3),
        },
        Qualifications =
        {
            new Qualification
            {
                Id = Guid.NewGuid(), Type = QualificationType.Degree,
                Name = "BSc Media Informatics", Institution = "HTW Berlin", Field = "Media Informatics",
                StartDate = new DateOnly(2014, 10, 1), EndDate = new DateOnly(2018, 7, 31),
            },
        },
        Experiences =
        {
            new Experience
            {
                Id = Guid.NewGuid(), Company = "PixelCraft", Title = "Frontend Engineer",
                Location = "Remote", StartDate = new DateOnly(2019, 3, 1), EndDate = null,
                Summary = "Owns the component library used across 4 products.",
                Achievements =
                {
                    Ach(1, "Built a 60-component design system in React + MUI adopted org-wide."),
                    Ach(2, "Reduced bundle size 35% via code-splitting and lazy routes."),
                },
                Skills = { XS(s["React"]), XS(s["TypeScript"]), XS(s["MUI"]) },
            },
        },
    };

    private static Employee BuildCarol(Dictionary<string, Skill> s) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Carol",
        LastName = "Almeida",
        Title = "Full-Stack Tech Lead",
        Email = "carol.almeida@example.com",
        Phone = "+1 555 0103",
        Location = "Lisbon, Portugal",
        Summary = "Full-stack lead bridging React frontends and .NET backends; mentors teams and owns delivery.",
        SpokenLanguages =
        {
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "English", Level = LanguageLevel.Fluent },
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "Portuguese", Level = LanguageLevel.Native },
            new SpokenLanguage { Id = Guid.NewGuid(), Language = "Spanish", Level = LanguageLevel.Conversational },
        },
        AvailabilityEntries =
        {
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2026, 1, 1), CapacityPercent = 0 },
            new AvailabilityEntry { Id = Guid.NewGuid(), EffectiveFrom = new DateOnly(2026, 9, 1), CapacityPercent = 100 },
        },
        Skills =
        {
            ES(s["React"], SkillLevel.Advanced, 5),
            ES(s["TypeScript"], SkillLevel.Advanced, 5),
            ES(s["C#"], SkillLevel.Advanced, 6),
            ES(s["ASP.NET Core"], SkillLevel.Advanced, 6),
            ES(s["PostgreSQL"], SkillLevel.Intermediate, 4),
            ES(s["Agile / Scrum"], SkillLevel.Expert, 7),
            ES(s["Test-Driven Development"], SkillLevel.Advanced, 5),
        },
        Qualifications =
        {
            new Qualification
            {
                Id = Guid.NewGuid(), Type = QualificationType.Degree,
                Name = "BSc Software Engineering", Institution = "University of Lisbon", Field = "Software Engineering",
                StartDate = new DateOnly(2010, 9, 1), EndDate = new DateOnly(2014, 6, 30),
            },
            new Qualification
            {
                Id = Guid.NewGuid(), Type = QualificationType.Certification,
                Name = "Professional Scrum Master I", Issuer = "Scrum.org",
                CredentialId = "PSM-I-55821", IssueDate = new DateOnly(2021, 5, 1), ExpiryDate = null,
            },
        },
        Experiences =
        {
            new Experience
            {
                Id = Guid.NewGuid(), Company = "Northwind Apps", Title = "Tech Lead",
                Location = "Lisbon", StartDate = new DateOnly(2021, 2, 1), EndDate = null,
                Summary = "Leads a cross-functional team of 6 delivering a SaaS product.",
                Achievements =
                {
                    Ach(1, "Introduced TDD and trunk-based development, cutting escaped defects by half."),
                    Ach(2, "Delivered full rewrite from legacy stack to React + ASP.NET Core in 9 months."),
                },
                Skills = { XS(s["React"]), XS(s["C#"]), XS(s["ASP.NET Core"]), XS(s["Agile / Scrum"]) },
            },
        },
    };
}
