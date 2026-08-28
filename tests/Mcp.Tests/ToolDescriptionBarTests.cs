using FluentAssertions;
using FluentAssertions.Execution;
using System.Text.RegularExpressions;
using Xunit;

namespace CvManager.Mcp.Tests;

/// <summary>
/// Pins the description bar (P1T-128 read clusters, P1T-129 writes): every rewritten tool must say
/// when NOT to use it and name the sibling it defers to, carry an inline input example, and state
/// what it does not return. The tool-selection eval (P1T-127) measures whether the bar works;
/// these tests stop it eroding silently — a well-meant tidy-up that drops "style_exemplar_search"
/// out of roster_semantic_search's description costs a whole eval cluster.
/// </summary>
public class ToolDescriptionBarTests
{
    /// <summary>Tool → substrings its description must contain. The sibling names are the
    /// disambiguation itself; the rest pin the input example and the negative space.</summary>
    private static readonly (string Tool, string[] MustContain)[] Bar =
    [
        ("employee_list", [
            "roster_semantic_search", "roster_shortlist_search", "roster_digest_list",
            "employee_get", "cv_get", "e.g. {}", "NO skills", "draft employees are excluded", "capacity"]),
        ("employee_get", [
            "roster_semantic_search", "roster_shortlist_search", "cv_get", "employee_list",
            "roster_digest_list", "\"id\":", "not_found", "no PDF", "availability step function"]),
        ("cv_get", [
            "roster_semantic_search", "roster_shortlist_search", "roster_digest_list",
            "employee_get", "style_exemplar_search", "achievementId", "\"employeeId\":",
            "not a PDF"]),
        ("roster_semantic_search", [
            "roster_shortlist_search", "style_exemplar_search", "roster_digest_list",
            "employee_get", "employee_list", "cv_get", "\"query\":", "semantic",
            "relevance score", "empty list"]),
        ("roster_shortlist_search", [
            "roster_semantic_search", "style_exemplar_search", "roster_digest_list",
            "employee_list", "cv_get", "\"requirements\":", "requirement", "coverage",
            "empty list"]),
        ("style_exemplar_search", [
            "roster_semantic_search", "roster_shortlist_search", "cv_get", "anonymized",
            "\"achievementIds\":", "PHRASING", "never"]),
        ("category_list", [
            "category_tree", "skill_list", "employee_get", "e.g. {}", "parentId", "no skills"]),
        ("category_tree", [
            "category_list", "skill_list", "employee_get", "e.g. {}", "TREE",
            "no employee data"]),
        ("skill_list", [
            "category_tree", "category_list", "employee_get", "skill_create",
            "employee_skill_add", "e.g. {}", "categoryId"]),
        // The reference implementation the bar was written from (P1T-121) — held to it too.
        ("roster_digest_list", ["roster_semantic_search", "cv_get", "bulk", "e.g. {"]),

        // ---- P1T-129: the write surface ----
        ("employee_create", [
            "employee_create_draft", "employee_update", "employee_skill_add", "language_add",
            "availability_add", "\"firstName\":", "ACTIVE", "unique"]),
        ("employee_create_draft", [
            "employee_create", "employee_update", "hidden", "HUMAN", "duplicateWarning",
            "\"firstName\":", "EMPTY email"]),
        ("employee_update", [
            "employee_get", "employee_skill_add", "availability_add", "employee_create",
            "full replace", "\"title\":", "not_found"]),
        ("employee_delete", [
            "availability_add", "employee_update", "DESTRUCTIVE", "admin scope", "\"id\":"]),
        ("employee_skill_add", [
            "skill_list", "skill_create", "experience_skill_add", "employee_skill_update",
            "PERSON", "\"skillId\":", "Advanced", "conflict"]),
        ("employee_skill_update", [
            "employee_skill_add", "skill_update", "employee_get", "\"level\":", "full replace"]),
        ("employee_skill_delete", [
            "skill_delete", "employee_skill_update", "DESTRUCTIVE", "admin scope"]),
        ("skill_create", [
            "employee_skill_add", "skill_list", "category_create", "CATALOG",
            "\"categoryId\":", "conflict"]),
        ("skill_update", [
            "employee_skill_update", "skill_create", "employee_skill_add", "\"name\":"]),
        ("skill_delete", [
            "employee_skill_delete", "skill_update", "DESTRUCTIVE", "admin scope"]),
        ("category_create", ["skill_create", "\"name\":", "parentId"]),
        ("category_update", ["skill_update", "\"parentId\":", "cycle"]),
        ("category_delete", [
            "skill_delete", "employee_skill_delete", "category_tree", "DESTRUCTIVE"]),
        ("language_add", [
            "employee_skill_add", "skill_create", "language_update", "\"language\":",
            "Professional", "programming language"]),
        ("language_update", ["language_add", "employee_get", "\"level\":", "full replace"]),
        ("language_delete", ["language_update", "DESTRUCTIVE", "admin scope"]),
        ("availability_list", [
            "roster_shortlist_search", "employee_list", "availableOn", "\"employeeId\":",
            "step function"]),
        ("availability_add", [
            "employee_update", "availability_update", "employee_delete", "step function",
            "\"effectiveFrom\":", "yyyy-MM-dd", "conflict"]),
        ("availability_update", [
            "availability_add", "availability_list", "yyyy-MM-dd", "full replace"]),
        ("availability_delete", [
            "availability_add", "availability_update", "DESTRUCTIVE", "admin scope"]),
        ("experience_add", [
            "achievement_add", "experience_skill_add", "experience_update", "\"company\":",
            "yyyy-MM-dd", "skillIds"]),
        ("experience_update", [
            "experience_add", "achievement_update", "full replace", "\"endDate\":"]),
        ("experience_delete", [
            "experience_update", "achievement_delete", "DESTRUCTIVE", "admin scope"]),
        ("achievement_add", [
            "experience_add", "achievement_update", "style_exemplar_search", "cv_get",
            "\"order\":", "achievementId"]),
        ("achievement_update", [
            "achievement_add", "experience_update", "cv_get", "\"text\":"]),
        ("achievement_delete", ["achievement_update", "DESTRUCTIVE", "admin scope"]),
        ("qualification_add", [
            "employee_skill_add", "experience_add", "qualification_update", "\"type\":",
            "Certification", "yyyy-MM-dd"]),
        ("qualification_update", [
            "qualification_add", "employee_get", "full replace", "\"issuer\":"]),
        ("qualification_delete", ["qualification_update", "DESTRUCTIVE", "admin scope"]),
        ("experience_skill_add", [
            "employee_skill_add", "skill_create", "skill_list", "\"experienceId\":",
            "conflict"]),
        ("experience_skill_delete", [
            "employee_skill_delete", "DESTRUCTIVE", "admin scope", "\"id\":"]),
    ];

    /// <summary>The two traps the P1T-112 audit called out by name: person-vs-catalog skills and
    /// create-vs-draft. Each tool must point AT its twin, so a model that reaches for the wrong
    /// one reads the correction in the description it is already looking at.</summary>
    private static readonly (string Tool, string MustNameTwin)[] Traps =
    [
        ("employee_skill_add", "skill_create"),
        ("skill_create", "employee_skill_add"),
        ("employee_create", "employee_create_draft"),
        ("employee_create_draft", "employee_create"),
        ("employee_skill_delete", "skill_delete"),
        ("skill_delete", "employee_skill_delete"),
    ];

    [Fact]
    public async Task Confusable_read_tools_name_their_siblings_and_show_an_input_example()
    {
        using var factory = McpTestHost.CreateFactory(
            nameof(Confusable_read_tools_name_their_siblings_and_show_an_input_example));
        await using var client = await McpTestHost.ConnectAsync(factory,
            McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope));

        var listed = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");

        using var _ = new AssertionScope();
        foreach (var (tool, mustContain) in Bar)
        {
            listed.Should().ContainKey(tool);
            if (!listed.TryGetValue(tool, out var description))
            {
                continue;
            }

            description.Should().Contain("Do NOT", $"{tool} must say when NOT to use it");
            description.Should().Contain("e.g.", $"{tool} must carry an inline input example");
            // Word-boundary match, since employee_create_draft legitimately contains
            // "employee_create" and employee_skill_delete contains "skill_delete".
            Regex.IsMatch(description, $@"(?<!\w){Regex.Escape(tool)}(?!\w)").Should()
                .BeFalse($"{tool} must not cite itself as the alternative");
            foreach (var fragment in mustContain)
            {
                description.Should().Contain(fragment, $"{tool}'s description bar requires it");
            }
        }
    }

    [Fact]
    public async Task The_named_traps_point_at_their_twin_in_both_directions()
    {
        using var factory = McpTestHost.CreateFactory(
            nameof(The_named_traps_point_at_their_twin_in_both_directions));
        await using var client = await McpTestHost.ConnectAsync(factory,
            McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope));

        var listed = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");

        using var _ = new AssertionScope();
        foreach (var (tool, twin) in Traps)
        {
            listed.Should().ContainKey(tool);
            listed.GetValueOrDefault(tool, "").Should()
                .Contain(twin, $"{tool} must name {twin} — that pair is the documented trap");
        }
    }

    [Fact]
    public async Task Every_destructive_tool_says_so_and_names_the_non_destructive_alternative()
    {
        using var factory = McpTestHost.CreateFactory(
            nameof(Every_destructive_tool_says_so_and_names_the_non_destructive_alternative));
        await using var client = await McpTestHost.ConnectAsync(factory,
            McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope));

        var destructive = (await client.ListToolsAsync())
            .Where(t => t.ProtocolTool.Annotations?.DestructiveHint == true)
            .ToList();

        destructive.Should().HaveCountGreaterThanOrEqualTo(10, "every family has a delete");

        using var _ = new AssertionScope();
        foreach (var tool in destructive)
        {
            var description = tool.Description ?? "";
            description.Should().Contain("DESTRUCTIVE", $"{tool.Name} must announce it");
            description.Should().Contain("Do NOT", $"{tool.Name} must offer the safer path");
            description.Should().Contain("admin scope", $"{tool.Name} must state the scope it needs");
        }
    }
}
