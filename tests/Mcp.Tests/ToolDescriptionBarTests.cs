using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CvManager.Mcp.Tests;

/// <summary>
/// Pins the P1T-128 description bar on the confusable READ clusters: every rewritten tool must
/// still say when NOT to use it and name the sibling it defers to, carry an inline input example,
/// and state what it does not return. The tool-selection eval (P1T-127) measures whether the bar
/// works; these tests stop it eroding silently — a well-meant tidy-up that drops
/// "style_exemplar_search" out of roster_semantic_search's description costs a whole eval cluster.
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
    ];

    [Fact]
    public async Task Confusable_read_tools_name_their_siblings_and_show_an_input_example()
    {
        using var factory = McpTestHost.CreateFactory(
            nameof(Confusable_read_tools_name_their_siblings_and_show_an_input_example));
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));

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
            description.Should().NotContain(tool, $"{tool} must not cite itself as the alternative");
            foreach (var fragment in mustContain)
            {
                description.Should().Contain(fragment, $"{tool}'s description bar requires it");
            }
        }
    }
}
