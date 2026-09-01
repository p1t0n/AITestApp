using ExpertToJob.Application.Common;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class ExperienceSkillServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"exp-skill-{Guid.NewGuid()}")
            .Options);

    private static ExperienceSkillService NewService(AppDbContext db) => new(db);

    private static async Task<(Experience exp, Skill skill)> Seed(AppDbContext db)
    {
        var expert = new Expert { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace", Email = "ada@x.com" };
        var exp = new Experience
        {
            Id = Guid.NewGuid(),
            ExpertId = expert.Id,
            Company = "Acme",
            Title = "Engineer",
            StartDate = new DateOnly(2020, 1, 1),
        };
        var category = new Category { Id = Guid.NewGuid(), Name = "Backend" };
        var skill = new Skill { Id = Guid.NewGuid(), Name = "C#", CategoryId = category.Id };
        db.Experts.Add(expert);
        db.Experiences.Add(exp);
        db.Categories.Add(category);
        db.Skills.Add(skill);
        await db.SaveChangesAsync();
        return (exp, skill);
    }

    [Fact]
    public async Task AddAsync_links_skill_to_experience()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var (exp, skill) = await Seed(db);

        var result = await svc.AddAsync(exp.Id, skill.Id);

        result.Id.Should().NotBeEmpty();
        result.SkillId.Should().Be(skill.Id);
        result.SkillName.Should().Be("C#");
    }

    [Fact]
    public async Task AddAsync_duplicate_link_throws_Conflict()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var (exp, skill) = await Seed(db);
        await svc.AddAsync(exp.Id, skill.Id);

        var act = () => svc.AddAsync(exp.Id, skill.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task AddAsync_unknown_skill_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var (exp, _) = await Seed(db);

        var act = () => svc.AddAsync(exp.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddAsync_unknown_experience_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var (_, skill) = await Seed(db);

        var act = () => svc.AddAsync(Guid.NewGuid(), skill.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_link()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var (exp, skill) = await Seed(db);
        var link = await svc.AddAsync(exp.Id, skill.Id);

        await svc.DeleteAsync(link.Id);

        (await db.ExperienceSkills.AnyAsync(x => x.Id == link.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_unknown_link_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
