using CvManager.Application.Common;
using CvManager.Application.Employees;
using CvManager.Domain.Entities;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CvManager.Application.Tests;

public class AchievementServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"achievement-{Guid.NewGuid()}")
            .Options);

    private static AchievementService NewService(AppDbContext db) =>
        new(db, new SaveAchievementValidator());

    private static async Task<Experience> SeedExperience(AppDbContext db)
    {
        var employee = new Employee { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace", Email = "ada@x.com" };
        var exp = new Experience
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Company = "Acme",
            Title = "Engineer",
            StartDate = new DateOnly(2020, 1, 1),
        };
        db.Employees.Add(employee);
        db.Experiences.Add(exp);
        await db.SaveChangesAsync();
        return exp;
    }

    [Fact]
    public async Task AddAsync_to_existing_experience_returns_achievement()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var exp = await SeedExperience(db);

        var result = await svc.AddAsync(exp.Id, new SaveAchievementDto(1, "Shipped X"));

        result.Id.Should().NotBeEmpty();
        result.Order.Should().Be(1);
        result.Text.Should().Be("Shipped X");
    }

    [Fact]
    public async Task AddAsync_to_unknown_experience_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.AddAsync(Guid.NewGuid(), new SaveAchievementDto(1, "Shipped X"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddAsync_with_empty_text_throws_ValidationException()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var exp = await SeedExperience(db);

        var act = () => svc.AddAsync(exp.Id, new SaveAchievementDto(1, ""));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_changes_text_and_order()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var exp = await SeedExperience(db);
        var added = await svc.AddAsync(exp.Id, new SaveAchievementDto(1, "Old"));

        var updated = await svc.UpdateAsync(added.Id, new SaveAchievementDto(5, "New"));

        updated.Order.Should().Be(5);
        updated.Text.Should().Be("New");
    }

    [Fact]
    public async Task UpdateAsync_unknown_achievement_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.UpdateAsync(Guid.NewGuid(), new SaveAchievementDto(1, "X"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_achievement()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var exp = await SeedExperience(db);
        var added = await svc.AddAsync(exp.Id, new SaveAchievementDto(1, "Bye"));

        await svc.DeleteAsync(added.Id);

        (await db.Achievements.AnyAsync(x => x.Id == added.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_unknown_achievement_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
