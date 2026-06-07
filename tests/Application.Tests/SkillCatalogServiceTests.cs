using EmployeeManager.Application.Common;
using EmployeeManager.Application.Skills;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EmployeeManager.Application.Tests;

public class SkillCatalogServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"catalog-{Guid.NewGuid()}")
            .Options);

    private static async Task<Category> AddCategory(AppDbContext db, string name, Guid? parentId = null)
    {
        var c = new Category { Id = Guid.NewGuid(), Name = name, ParentId = parentId };
        db.Categories.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task ReParent_under_self_is_rejected()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var cat = await AddCategory(db, "Languages");

        var act = () => svc.UpdateCategoryAsync(cat.Id, new SaveCategoryDto("Languages", cat.Id));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ReParent_under_own_descendant_is_rejected()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var root = await AddCategory(db, "Languages");
        var child = await AddCategory(db, "JavaScript", root.Id);
        var grandchild = await AddCategory(db, "Frontend", child.Id);

        // Move root under its own grandchild — would create a cycle.
        var act = () => svc.UpdateCategoryAsync(root.Id, new SaveCategoryDto("Languages", grandchild.Id));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ReParent_to_unrelated_category_succeeds()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var a = await AddCategory(db, "A");
        var b = await AddCategory(db, "B");

        var result = await svc.UpdateCategoryAsync(b.Id, new SaveCategoryDto("B", a.Id));

        result.ParentId.Should().Be(a.Id);
    }

    [Fact]
    public async Task CreateCategory_duplicate_sibling_name_is_rejected_case_insensitively()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var parent = await AddCategory(db, "Languages");
        await svc.CreateCategoryAsync(new SaveCategoryDto("JavaScript", parent.Id));

        var act = () => svc.CreateCategoryAsync(new SaveCategoryDto("javascript", parent.Id));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateCategory_same_name_under_different_parent_succeeds()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var frontend = await AddCategory(db, "Frontend");
        var mobile = await AddCategory(db, "Mobile");

        await svc.CreateCategoryAsync(new SaveCategoryDto("React", frontend.Id));
        var act = () => svc.CreateCategoryAsync(new SaveCategoryDto("React", mobile.Id));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateCategory_rename_to_self_is_allowed()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var cat = await AddCategory(db, "Languages");

        var act = () => svc.UpdateCategoryAsync(cat.Id, new SaveCategoryDto("Languages", null));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateSkill_same_name_in_different_category_succeeds()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var data = await AddCategory(db, "Data");
        var lang = await AddCategory(db, "Languages");

        await svc.CreateSkillAsync(new SaveSkillDto("SQL", data.Id));
        var act = () => svc.CreateSkillAsync(new SaveSkillDto("SQL", lang.Id));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateSkill_duplicate_name_in_same_category_is_rejected()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var data = await AddCategory(db, "Data");
        await svc.CreateSkillAsync(new SaveSkillDto("PostgreSQL", data.Id));

        var act = () => svc.CreateSkillAsync(new SaveSkillDto("postgresql", data.Id));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateSkill_move_to_another_category_recomputes_category_name()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var data = await AddCategory(db, "Data");
        var backend = await AddCategory(db, "Backend");
        var skill = await svc.CreateSkillAsync(new SaveSkillDto("PostgreSQL", data.Id));

        var moved = await svc.UpdateSkillAsync(skill.Id, new SaveSkillDto("PostgreSQL", backend.Id));

        moved.CategoryId.Should().Be(backend.Id);
        moved.CategoryName.Should().Be("Backend");
    }

    [Fact]
    public async Task GetTree_orders_skills_by_rank_desc_then_name_asc()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var cat = await AddCategory(db, "Data");
        db.Skills.AddRange(
            new Skill { Id = Guid.NewGuid(), Name = "Alpha", CategoryId = cat.Id, Rank = 1 },
            new Skill { Id = Guid.NewGuid(), Name = "Zeta", CategoryId = cat.Id, Rank = 5 },
            new Skill { Id = Guid.NewGuid(), Name = "Beta", CategoryId = cat.Id, Rank = 5 });
        await db.SaveChangesAsync();

        var tree = await svc.GetTreeAsync();

        var names = tree.Single(n => n.Id == cat.Id).Skills.Select(s => s.Name);
        names.Should().ContainInOrder("Beta", "Zeta", "Alpha"); // rank 5 (Beta<Zeta), then rank 1
    }

    [Fact]
    public async Task UpdateSkill_preserves_existing_rank()
    {
        await using var db = NewDb();
        var svc = new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
        var cat = await AddCategory(db, "Data");
        var skill = new Skill { Id = Guid.NewGuid(), Name = "SQL", CategoryId = cat.Id, Rank = 7 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var updated = await svc.UpdateSkillAsync(skill.Id, new SaveSkillDto("Structured Query Language", cat.Id));

        updated.Rank.Should().Be(7);
        (await db.Skills.FindAsync(skill.Id))!.Rank.Should().Be(7);
    }
}
