using System.Collections.Generic;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EmployeeManager.Mcp.Tests;

public class AllToolsTests
{
    private static readonly string[] ExpectedTools =
    {
        "employee_list", "employee_get", "employee_create", "employee_update", "employee_delete",
        "language_add", "language_update", "language_delete",
        "availability_list", "availability_add", "availability_update", "availability_delete",
        "employee_skill_add", "employee_skill_update", "employee_skill_delete",
        "qualification_add", "qualification_update", "qualification_delete",
        "experience_add", "experience_update", "experience_delete",
        "achievement_add", "achievement_update", "achievement_delete",
        "experience_skill_add", "experience_skill_delete",
        "category_list", "category_tree", "category_create", "category_update", "category_delete",
        "skill_list", "skill_create", "skill_update", "skill_delete",
        "cv_get",
        "roster_semantic_search", "roster_shortlist_search",
    };

    private static async Task<CallToolResult> Call(McpClient c, string name, Dictionary<string, object?>? args = null) =>
        await c.CallToolAsync(name, args ?? new Dictionary<string, object?>());

    [Fact]
    public async Task All_expected_tools_are_registered()
    {
        using var factory = McpTestHost.CreateFactory(nameof(All_expected_tools_are_registered));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var names = (await client.ListToolsAsync()).Select(t => t.Name);

        names.Should().Contain(ExpectedTools);
    }

    [Fact]
    public async Task Catalog_create_list_update_delete_round_trip()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Catalog_create_list_update_delete_round_trip));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var category = await Call(client, "category_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "Backend" } });
        var categoryId = McpTestHost.IdOf(category);

        var skill = await Call(client, "skill_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "C#", ["categoryId"] = categoryId } });
        skill.IsError.Should().NotBe(true);
        var skillId = McpTestHost.IdOf(skill);

        var skills = await Call(client, "skill_list");
        McpTestHost.Text(skills).Should().Contain("C#");

        var updated = await Call(client, "skill_update",
            new() { ["id"] = skillId, ["dto"] = new Dictionary<string, object?> { ["name"] = "CSharp", ["categoryId"] = categoryId } });
        McpTestHost.Text(updated).Should().Contain("CSharp");

        var deletedSkill = await Call(client, "skill_delete", new() { ["id"] = skillId });
        deletedSkill.IsError.Should().NotBe(true);
        var deletedCategory = await Call(client, "category_delete", new() { ["id"] = categoryId });
        deletedCategory.IsError.Should().NotBe(true);
    }

    [Fact]
    public async Task Duplicate_skill_name_in_category_returns_conflict()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Duplicate_skill_name_in_category_returns_conflict));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var category = await Call(client, "category_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "Data" } });
        var categoryId = McpTestHost.IdOf(category);
        await Call(client, "skill_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "SQL", ["categoryId"] = categoryId } });

        var dup = await Call(client, "skill_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "sql", ["categoryId"] = categoryId } });

        dup.IsError.Should().BeTrue();
        McpTestHost.Text(dup).Should().Contain("conflict");
    }

    [Fact]
    public async Task Full_employee_graph_flows_into_cv()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Full_employee_graph_flows_into_cv));
        var employee = McpTestHost.SeedEmployee(factory);
        var empId = employee.Id.ToString();
        await using var client = await McpTestHost.ConnectAsync(factory);

        var category = await Call(client, "category_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "Backend" } });
        var skill = await Call(client, "skill_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "C#", ["categoryId"] = McpTestHost.IdOf(category) } });
        var skillId = McpTestHost.IdOf(skill);

        (await Call(client, "employee_skill_add", new()
        {
            ["employeeId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["skillId"] = skillId, ["level"] = "Advanced", ["yearsExperience"] = 5 },
        })).IsError.Should().NotBe(true);

        (await Call(client, "language_add", new()
        {
            ["employeeId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["language"] = "English", ["level"] = "Fluent" },
        })).IsError.Should().NotBe(true);

        (await Call(client, "availability_add", new()
        {
            ["employeeId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["effectiveFrom"] = "2027-01-01", ["capacityPercent"] = 50 },
        })).IsError.Should().NotBe(true);

        (await Call(client, "qualification_add", new()
        {
            ["employeeId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["type"] = "Degree", ["name"] = "MSc Computer Science" },
        })).IsError.Should().NotBe(true);

        var experience = await Call(client, "experience_add", new()
        {
            ["employeeId"] = empId,
            ["dto"] = new Dictionary<string, object?>
            {
                ["company"] = "Acme",
                ["title"] = "Senior Engineer",
                ["startDate"] = "2020-01-01",
                ["achievements"] = new List<object?>(),
                ["skillIds"] = new List<object?>(),
            },
        });
        experience.IsError.Should().NotBe(true);
        var experienceId = McpTestHost.IdOf(experience);

        (await Call(client, "achievement_add", new()
        {
            ["experienceId"] = experienceId,
            ["dto"] = new Dictionary<string, object?> { ["order"] = 1, ["text"] = "Shipped the billing rewrite" },
        })).IsError.Should().NotBe(true);

        (await Call(client, "experience_skill_add", new()
        {
            ["experienceId"] = experienceId,
            ["skillId"] = skillId,
        })).IsError.Should().NotBe(true);

        var cv = await Call(client, "cv_get", new() { ["employeeId"] = empId });

        cv.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(cv);
        text.Should().Contain("Acme");
        text.Should().Contain("Shipped the billing rewrite");
        text.Should().Contain("English");
    }
}
