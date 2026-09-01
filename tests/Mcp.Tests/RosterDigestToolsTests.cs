using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The roster_digest_list MCP tool (P1T-121) end-to-end over the MCP transport against the
/// in-memory database: exposure + scope, paging/stable order, digest content sourced from the
/// same narrative rendering semantic search embeds, draft exclusion, and the empty shape.
/// </summary>
public class RosterDigestToolsTests
{
    [Fact]
    public async Task Tool_is_exposed_under_the_read_scope_with_a_disambiguating_description()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Tool_is_exposed_under_the_read_scope_with_a_disambiguating_description));
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var tool = (await client.ListToolsAsync()).SingleOrDefault(t => t.Name == "roster_digest_list");

        tool.Should().NotBeNull();
        // The P1T-112 description bar: when to use it AND when not, with the siblings named.
        tool!.Description.Should().Contain("bulk")
            .And.Contain("roster_semantic_search")
            .And.Contain("cv_get");
    }

    [Fact]
    public async Task Pages_active_experts_in_stable_id_order_with_narrative_digests()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Pages_active_experts_in_stable_id_order_with_narrative_digests));
        var ids = await SeedAsync(factory, activeCount: 3, draftCount: 1);
        await using var client = await McpTestHost.ConnectAsync(factory);

        var page1 = McpTestHost.Text(await client.CallToolAsync("roster_digest_list",
            new Dictionary<string, object?> { ["page"] = 1, ["pageSize"] = 2 }));
        var page2 = McpTestHost.Text(await client.CallToolAsync("roster_digest_list",
            new Dictionary<string, object?> { ["page"] = 2, ["pageSize"] = 2 }));

        // The draft never appears; total counts actives only.
        page1.Should().Contain("\"total\":3").And.NotContain("Draft Dana");
        // Stable id order across pages: 2 + 1 split, no repeats.
        var ordered = ids.OrderBy(x => x).ToList();
        page1.Should().Contain(ordered[0].ToString()).And.Contain(ordered[1].ToString());
        page2.Should().Contain(ordered[2].ToString()).And.NotContain(ordered[0].ToString());

        // Digest carries the narrative units: summary text and the experience header + bullet.
        page1.Should().Contain("Veteran stream wrangler")
            .And.Contain("Platform Lead @ FlowWorks")
            .And.Contain("Cut deploy time by 40%");

        // And not the fields the description promises to exclude.
        page1.Should().NotContain("email").And.NotContain("capacity");
    }

    [Fact]
    public async Task Empty_roster_returns_an_empty_page_with_zero_total()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Empty_roster_returns_an_empty_page_with_zero_total));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("roster_digest_list", new Dictionary<string, object?>());

        result.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(result);
        text.Should().Contain("\"total\":0").And.Contain("\"items\":[]");
    }

    private static async Task<List<Guid>> SeedAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, int activeCount, int draftCount)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ids = new List<Guid>();
        for (var i = 0; i < activeCount; i++)
        {
            var expert = new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = $"Active{i}",
                LastName = "Person",
                Title = "Engineer",
                Email = $"active{i}@example.com",
                Status = ExpertStatus.Active,
                Summary = "Veteran stream wrangler across payments platforms.",
                Experiences =
                {
                    new Experience
                    {
                        Id = Guid.NewGuid(),
                        Title = "Platform Lead",
                        Company = "FlowWorks",
                        StartDate = new DateOnly(2019, 3, 1),
                        Summary = "Ran the streaming platform.",
                        Achievements = { new Achievement { Id = Guid.NewGuid(), Text = "Cut deploy time by 40%", Order = 1 } },
                    },
                },
            };
            ids.Add(expert.Id);
            db.Experts.Add(expert);
        }

        for (var i = 0; i < draftCount; i++)
        {
            db.Experts.Add(new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = "Draft",
                LastName = "Dana",
                Title = "Engineer",
                Email = $"draft{i}@example.com",
                Status = ExpertStatus.Draft,
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }
}
