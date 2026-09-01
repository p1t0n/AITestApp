using System.Collections.Generic;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

public class AllToolsTests
{
    private static readonly string[] ExpectedTools =
    {
        "expert_list", "expert_get", "expert_create", "expert_update", "expert_delete",
        "language_add", "language_update", "language_delete",
        "availability_list", "availability_add", "availability_update", "availability_delete",
        "expert_skill_add", "expert_skill_update", "expert_skill_delete",
        "qualification_add", "qualification_update", "qualification_delete",
        "experience_add", "experience_update", "experience_delete",
        "achievement_add", "achievement_update", "achievement_delete",
        "experience_skill_add", "experience_skill_delete",
        "category_list", "category_tree", "category_create", "category_update", "category_delete",
        "skill_list", "skill_create", "skill_update", "skill_delete",
        "cv_get",
        "roster_semantic_search", "roster_shortlist_search", "style_exemplar_search",
        "roster_digest_list",
    };

    private static async Task<CallToolResult> Call(McpClient c, string name, Dictionary<string, object?>? args = null) =>
        await c.CallToolAsync(name, args ?? new Dictionary<string, object?>());

    /// <summary>
    /// The rename's own guard (P1T-177). The registry is the contract an agent's token is filtered
    /// against, so a tool left behind under its old name is not a cosmetic miss: the Keycloak grant
    /// is <c>mcp:tool:expert_list</c>, and a server still advertising <c>employee_list</c> hands
    /// the agent a tool its token cannot carry. Asserting the absence is what catches the
    /// half-rename that asserting the presence cannot.
    ///
    /// <para>The retired token below is spelled from its halves on purpose. This assertion is the
    /// one place in the tree that must keep naming the old vocabulary, and a repo-wide search and
    /// replace — the very thing it exists to check — would otherwise rewrite it into a test that
    /// forbids the new name instead. It ate this test once already.</para>
    /// </summary>
    [Fact]
    public async Task No_tool_is_still_named_after_the_retired_entity()
    {
        const string retired = "emp" + "loyee";

        using var factory = McpTestHost.CreateFactory(nameof(No_tool_is_still_named_after_the_retired_entity));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().NotBeEmpty("an empty registry would pass this vacuously");
        names.Should().OnlyContain(n => !n.Contains(retired, StringComparison.OrdinalIgnoreCase));
    }

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
    public async Task Full_expert_graph_flows_into_cv()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Full_expert_graph_flows_into_cv));
        var expert = McpTestHost.SeedExpert(factory);
        var empId = expert.Id.ToString();
        await using var client = await McpTestHost.ConnectAsync(factory);

        var category = await Call(client, "category_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "Backend" } });
        var skill = await Call(client, "skill_create",
            new() { ["dto"] = new Dictionary<string, object?> { ["name"] = "C#", ["categoryId"] = McpTestHost.IdOf(category) } });
        var skillId = McpTestHost.IdOf(skill);

        (await Call(client, "expert_skill_add", new()
        {
            ["expertId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["skillId"] = skillId, ["level"] = "Advanced", ["yearsExperience"] = 5 },
        })).IsError.Should().NotBe(true);

        (await Call(client, "language_add", new()
        {
            ["expertId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["language"] = "English", ["level"] = "Fluent" },
        })).IsError.Should().NotBe(true);

        (await Call(client, "availability_add", new()
        {
            ["expertId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["effectiveFrom"] = "2027-01-01", ["capacityPercent"] = 50 },
        })).IsError.Should().NotBe(true);

        (await Call(client, "qualification_add", new()
        {
            ["expertId"] = empId,
            ["dto"] = new Dictionary<string, object?> { ["type"] = "Degree", ["name"] = "MSc Computer Science" },
        })).IsError.Should().NotBe(true);

        var experience = await Call(client, "experience_add", new()
        {
            ["expertId"] = empId,
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

        var cv = await Call(client, "cv_get", new() { ["expertId"] = empId });

        cv.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(cv);
        text.Should().Contain("Acme");
        text.Should().Contain("Shipped the billing rewrite");
        text.Should().Contain("English");
    }
}
