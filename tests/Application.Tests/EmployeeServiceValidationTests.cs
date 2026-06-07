using EmployeeManager.Application.Employees;
using EmployeeManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EmployeeManager.Application.Tests;

public class EmployeeServiceValidationTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"employee-{Guid.NewGuid()}")
            .Options);

    private static EmployeeService NewService(AppDbContext db) =>
        new(db, new SaveEmployeeValidator());

    private static SaveEmployeeDto Invalid =>
        new("", "X", "T", "not-an-email", null, null, null, null);

    [Fact]
    public async Task CreateAsync_with_invalid_input_throws_ValidationException()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.CreateAsync(Invalid);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_with_invalid_input_throws_ValidationException()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var created = await svc.CreateAsync(
            new SaveEmployeeDto("Ada", "Lovelace", "Engineer", "ada@example.com", null, null, null, null));

        var act = () => svc.UpdateAsync(created.Id, Invalid);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task CreateAsync_with_valid_input_succeeds()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var created = await svc.CreateAsync(
            new SaveEmployeeDto("Ada", "Lovelace", "Engineer", "ada@example.com", null, "London", null, null));

        created.Id.Should().NotBeEmpty();
        created.Email.Should().Be("ada@example.com");
    }
}
