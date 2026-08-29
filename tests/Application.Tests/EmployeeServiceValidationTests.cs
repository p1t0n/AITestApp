using CvManager.Application.Common;
using CvManager.Application.Employees;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CvManager.Application.Tests;

public class EmployeeServiceValidationTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"employee-{Guid.NewGuid()}")
            .Options);

    private static EmployeeService NewService(AppDbContext db) =>
        new(db, new SaveEmployeeValidator(), new UpdateEmployeeValidator());

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

    [Fact]
    public async Task PatchAsync_with_only_title_set_changes_title_and_leaves_other_fields_untouched()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var created = await svc.CreateAsync(
            new SaveEmployeeDto("Ada", "Lovelace", "Engineer", "ada@example.com", "555-1234", "London", "Bio", null));

        var patched = await svc.PatchAsync(created.Id, new UpdateEmployeeDto(
            FirstName: null, LastName: null, Title: "Staff Engineer", Email: null,
            Phone: null, Location: null, Summary: null, PhotoUrl: null));

        patched.Title.Should().Be("Staff Engineer");
        patched.FirstName.Should().Be("Ada");
        patched.LastName.Should().Be("Lovelace");
        patched.Email.Should().Be("ada@example.com");
        patched.Phone.Should().Be("555-1234");
        patched.Location.Should().Be("London");
        patched.Summary.Should().Be("Bio");
    }

    [Fact]
    public async Task PatchAsync_with_empty_first_name_throws_ValidationException()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var created = await svc.CreateAsync(
            new SaveEmployeeDto("Ada", "Lovelace", "Engineer", "ada@example.com", null, null, null, null));

        var act = () => svc.PatchAsync(created.Id, new UpdateEmployeeDto(
            FirstName: "", LastName: null, Title: null, Email: null,
            Phone: null, Location: null, Summary: null, PhotoUrl: null));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task PatchAsync_with_unknown_id_throws_NotFoundException()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.PatchAsync(Guid.NewGuid(), new UpdateEmployeeDto(
            FirstName: null, LastName: null, Title: "Staff Engineer", Email: null,
            Phone: null, Location: null, Summary: null, PhotoUrl: null));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
