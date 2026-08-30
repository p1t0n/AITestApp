using System.Text.Json;
using CvManager.CostFloors;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace CvManager.Mcp.Tests.CostFloors;

/// <summary>
/// The schema half of the Cost Floors (P1T-144): what the read surface costs merely by being
/// offered. Twenty-six percent of the 160,220-token roster-qa run was this — eleven tool schemas
/// re-sent on all ten iterations — and it grew through P1T-128/129 with a green suite, because
/// nothing measured it. Nothing here calls a model: the tool listing is the measurement.
/// </summary>
public class ToolSurfaceCostFloorTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Every_tool_schema_stays_under_its_ratcheted_ceiling()
    {
        var schemas = await ToolSchemasAsync(nameof(Every_tool_schema_stays_under_its_ratcheted_ceiling));

        using var _ = new AssertionScope();
        foreach (var (tool, text) in schemas.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var tokens = TokenEstimate.Of(text);
            output.WriteLine($"{tool,-28} {tokens,6} tokens");

            CvManager.CostFloors.CostFloors.ToolSchemaCeilings.Should().ContainKey(
                tool,
                "a tool with no ratcheted schema ceiling ships unmeasured — pin it in CostFloors");
            if (CvManager.CostFloors.CostFloors.ToolSchemaCeilings.TryGetValue(tool, out var ceiling))
            {
                tokens.Should().BeLessThanOrEqualTo(
                    ceiling,
                    $"{tool}'s schema is paid on every iteration of every agent that is shown it");
            }
        }
    }

    [Fact]
    public async Task The_whole_read_surface_stays_under_its_ratcheted_ceiling()
    {
        var schemas = await ToolSchemasAsync(nameof(The_whole_read_surface_stays_under_its_ratcheted_ceiling), readOnly: true);

        var total = schemas.Sum(p => TokenEstimate.Of(p.Value));
        output.WriteLine($"read surface: {schemas.Count} tools, {total} tokens");

        // roster-qa is still shown all of this, so this total IS its schema cost per iteration.
        total.Should().BeLessThanOrEqualTo(CvManager.CostFloors.CostFloors.ReadToolSurfaceCeiling);
    }

    [Fact]
    public async Task The_declared_scope_surfaces_match_what_the_server_advertises()
    {
        // Agents.Tests measures each agent's Baseline Prompt Size against the surface its own
        // token would carry. That declaration lives in CostFloors, where no MCP server can be
        // reached — so this is what keeps it honest against the real scope policy.
        using var factory = McpTestHost.CreateFactory(nameof(The_declared_scope_surfaces_match_what_the_server_advertises));

        await using var readClient = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));
        var read = (await readClient.ListToolsAsync()).Select(t => t.Name);

        await using var writeClient = await McpTestHost.ConnectAsync(
            factory, McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope));
        var write = (await writeClient.ListToolsAsync()).Select(t => t.Name);

        using var _ = new AssertionScope();
        read.Should().BeEquivalentTo(CvManager.CostFloors.CostFloors.ReadScopeTools);
        write.Should().BeEquivalentTo(CvManager.CostFloors.CostFloors.WriteScopeTools);
    }

    /// <summary>Every tool the token is shown, serialized the way the model sees it. EF InMemory is
    /// enough — a schema does not depend on the data behind it.</summary>
    private static async Task<Dictionary<string, string>> ToolSchemasAsync(string dbName, bool readOnly = false)
    {
        using var factory = McpTestHost.CreateFactory(dbName);
        var token = readOnly
            ? McpTestHost.MintToken(McpTestHost.ReadScope)
            : McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope);
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        return (await client.ListToolsAsync()).ToDictionary(
            t => t.Name,
            t => ToolSurface.SchemaText(
                t.Name,
                t.Description,
                JsonSerializer.Serialize(t.ProtocolTool.InputSchema)));
    }
}
