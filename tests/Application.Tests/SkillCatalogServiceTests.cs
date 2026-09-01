using ExpertToJob.Application.Common;
using ExpertToJob.Application.Skills;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

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

    // ---- P1T-145: filtering and paging the catalog ------------------------------------------

    private static async Task<SkillCatalogService> WithSkills(AppDbContext db, params string[] names)
    {
        var cat = await AddCategory(db, "Frontend");
        db.Skills.AddRange(names.Select(n => new Skill { Id = Guid.NewGuid(), Name = n, CategoryId = cat.Id }));
        await db.SaveChangesAsync();
        return new SkillCatalogService(db, new SaveCategoryValidator(), new SaveSkillValidator());
    }

    [Fact]
    public async Task NameContains_matches_a_case_insensitive_substring()
    {
        await using var db = NewDb();
        var svc = await WithSkills(db, "React", "React Native", "Vue", "Angular");

        var page = await svc.SearchSkillsAsync(new SkillQuery(NameContains: "reAct"));

        page.Items.Select(s => s.Name).Should().BeEquivalentTo("React", "React Native");
        page.Total.Should().Be(2, "total counts the matches, not the catalog");
    }

    [Fact]
    public async Task NameContains_that_matches_nothing_returns_an_empty_page_not_the_catalog()
    {
        await using var db = NewDb();
        var svc = await WithSkills(db, "React", "Vue");

        var page = await svc.SearchSkillsAsync(new SkillQuery(NameContains: "cobol"));

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task A_blank_filter_is_treated_as_no_filter()
    {
        await using var db = NewDb();
        var svc = await WithSkills(db, "React", "Vue");

        (await svc.SearchSkillsAsync(new SkillQuery(NameContains: "   "))).Total.Should().Be(2);
        (await svc.SearchSkillsAsync(new SkillQuery())).Total.Should().Be(2);
    }

    [Fact]
    public async Task Paging_walks_the_matches_in_rank_order_and_reports_the_full_total()
    {
        await using var db = NewDb();
        var svc = await WithSkills(db, "Aaa", "Bbb", "Ccc", "Ddd", "Eee");

        var first = await svc.SearchSkillsAsync(new SkillQuery(Page: 1, PageSize: 2));
        var second = await svc.SearchSkillsAsync(new SkillQuery(Page: 2, PageSize: 2));
        var past = await svc.SearchSkillsAsync(new SkillQuery(Page: 9, PageSize: 2));

        first.Items.Select(s => s.Name).Should().ContainInOrder("Aaa", "Bbb");
        second.Items.Select(s => s.Name).Should().ContainInOrder("Ccc", "Ddd");
        past.Items.Should().BeEmpty("a page past the end is empty, not an error");
        new[] { first, second, past }.Should().OnlyContain(p => p.Total == 5);
    }

    [Fact]
    public async Task Page_and_pageSize_are_clamped_rather_than_rejected()
    {
        await using var db = NewDb();
        var svc = await WithSkills(db, "Aaa", "Bbb");

        // A model that guesses 0 or a negative gets the first page, not a validation error to
        // burn a retry on; an oversized request is capped instead of returning the whole table.
        var clamped = await svc.SearchSkillsAsync(new SkillQuery(Page: 0, PageSize: 0));
        clamped.Page.Should().Be(1);
        clamped.PageSize.Should().Be(1);

        var capped = await svc.SearchSkillsAsync(new SkillQuery(PageSize: 10_000));
        capped.PageSize.Should().Be(SkillCatalogService.MaxPageSize);
    }

    [Fact]
    public async Task The_default_page_holds_the_whole_seeded_catalog()
    {
        // ResumeIngestionAgent's step 1 is a single unfiltered skill_list, and it matches resume
        // skills against exactly what comes back. The committed dataset carries 79 skills; if it
        // ever outgrows the default page, that agent starts silently missing skills — so this,
        // not a production incident, is what says the default needs raising.
        var catalogSkills = DemoRosterSeeder.LoadCommittedDataset().Skills.Count;

        catalogSkills.Should().BeLessThanOrEqualTo(SkillCatalogService.DefaultPageSize);
    }
}
