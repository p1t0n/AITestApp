using CvManager.Application.Employees;
using CvManager.Domain.Entities;
using CvManager.Domain.Enums;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CvManager.Application.Tests;

public class ExperienceServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"experience-{Guid.NewGuid()}")
            .Options);

    private static ExperienceService NewService(AppDbContext db) =>
        new(db, new SaveExperienceValidator());

    private static async Task<(Employee Employee, Skill Skill)> Seed(AppDbContext db)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Backend" };
        var skill = new Skill { Id = Guid.NewGuid(), Name = "C#", CategoryId = category.Id };
        var employee = new Employee { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace", Email = "ada@x.com" };
        db.Categories.Add(category);
        db.Skills.Add(skill);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return (employee, skill);
    }

    private static SaveExperienceDto Dto(Guid skillId, params string[] bullets) => new(
        "Acme", "Engineer", null, new DateOnly(2020, 1, 1), null, "Did things.",
        bullets.Select((t, i) => new SaveAchievementDto(i + 1, t)).ToList(),
        [skillId]);

    [Fact]
    public async Task UpdateAsync_replaces_children_when_the_experience_already_has_them()
    {
        // Regression: the update path clears the loaded children and re-adds new ones through the
        // navigation. Entities discovered via fixup with a pre-set key are tracked as Modified, not
        // Added — EF then UPDATEs rows that don't exist and throws DbUpdateConcurrencyException.
        // Surfaced by the Tailor CV "Apply" flow (the first real caller of this PUT with children).
        await using var db = NewDb();
        var svc = NewService(db);
        var (employee, skill) = await Seed(db);

        var created = await svc.AddAsync(employee.Id, Dto(skill.Id, "Old bullet one", "Old bullet two"));

        var updated = await svc.UpdateAsync(created.Id, Dto(skill.Id, "New bullet one", "New bullet two"));

        updated.Achievements.Select(a => a.Text).Should().Equal("New bullet one", "New bullet two");
        (await db.Achievements.CountAsync(a => a.ExperienceId == created.Id)).Should().Be(2);
        (await db.ExperienceSkills.CountAsync(s => s.ExperienceId == created.Id)).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_applies_a_single_bullet_text_change_keeping_siblings()
    {
        // The Apply-rewrite shape: same experience, same skills, one bullet's text swapped.
        await using var db = NewDb();
        var svc = NewService(db);
        var (employee, skill) = await Seed(db);
        var created = await svc.AddAsync(employee.Id, Dto(skill.Id, "Keep me", "Rewrite me"));

        var updated = await svc.UpdateAsync(created.Id, Dto(skill.Id, "Keep me", "Rewritten, sharper"));

        updated.Achievements.Select(a => a.Text).Should().Equal("Keep me", "Rewritten, sharper");
    }
}
