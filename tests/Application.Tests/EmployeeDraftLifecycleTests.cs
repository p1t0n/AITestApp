using CvManager.Application.Common;
using CvManager.Application.Employees;
using CvManager.Domain.Entities;
using CvManager.Domain.Enums;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvManager.Application.Tests;

/// <summary>
/// The draft-then-promote lifecycle (P1T-92): agent-staged drafts are invisible to the default
/// roster list, carry a duplicate warning when a same-name employee exists, may lack an email
/// while Draft, and only cross into Active through the promote gate — which demands one.
/// </summary>
public class EmployeeDraftLifecycleTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"draft-{Guid.NewGuid()}")
            .Options);

    private static EmployeeService NewService(AppDbContext db) =>
        new(db, new SaveEmployeeValidator());

    private static SaveEmployeeDto Dto(string first = "Torvald", string last = "Emberwright", string email = "t@example.com") =>
        new(first, last, "Senior Engineer", email, null, null, null, null);

    [Fact]
    public async Task Drafts_are_hidden_from_the_default_list_but_visible_on_request()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        await svc.CreateAsync(Dto("Alice", "Active", "a@example.com"));
        var draft = await svc.CreateDraftAsync(Dto("Danny", "Draft", "d@example.com"));

        (await svc.ListAsync()).Should().OnlyContain(e => e.Status == EmployeeStatus.Active);
        (await svc.ListAsync(includeDrafts: true)).Should()
            .Contain(e => e.Id == draft.Employee.Id && e.Status == EmployeeStatus.Draft);
    }

    [Fact]
    public async Task Draft_create_accepts_a_missing_email_and_warns_on_a_same_name_duplicate()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        await svc.CreateAsync(Dto());

        var draft = await svc.CreateDraftAsync(Dto(email: ""));

        draft.Employee.Status.Should().Be(EmployeeStatus.Draft);
        draft.Employee.Email.Should().BeEmpty("the resume had no email and none may be invented");
        draft.DuplicateWarning.Should().Contain("Torvald Emberwright");
    }

    [Fact]
    public async Task Draft_create_carries_no_warning_when_the_name_is_new()
    {
        await using var db = NewDb();
        var draft = await NewService(db).CreateDraftAsync(Dto());
        draft.DuplicateWarning.Should().BeNull();
    }

    [Fact]
    public async Task Promote_flips_a_draft_to_active_and_is_idempotent()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var draft = await svc.CreateDraftAsync(Dto());

        var promoted = await svc.PromoteAsync(draft.Employee.Id);
        promoted.Status.Should().Be(EmployeeStatus.Active);

        var again = await svc.PromoteAsync(draft.Employee.Id);
        again.Status.Should().Be(EmployeeStatus.Active);
        (await svc.ListAsync()).Should().ContainSingle(e => e.Id == draft.Employee.Id);
    }

    [Fact]
    public async Task Promote_refuses_a_draft_without_a_valid_email()
    {
        await using var db = NewDb();
        var svc = NewService(db);
        var draft = await svc.CreateDraftAsync(Dto(email: ""));

        var act = () => svc.PromoteAsync(draft.Employee.Id);

        await act.Should().ThrowAsync<ValidationException>();
        (await svc.ListAsync()).Should().NotContain(e => e.Id == draft.Employee.Id,
            "a refused promote must leave the draft hidden");
    }

    [Fact]
    public async Task Promote_of_an_unknown_id_reports_not_found()
    {
        await using var db = NewDb();
        var act = () => NewService(db).PromoteAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
