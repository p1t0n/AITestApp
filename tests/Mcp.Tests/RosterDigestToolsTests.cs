using ExpertToJob.Application.Compliance;
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
/// same narrative rendering semantic search embeds, draft exclusion, the empty shape — and, since
/// this page <em>is</em> the Roster Scan's candidate enumeration, the Art. 22 route filter that
/// keeps legitimate-interest rows out of it entirely (P1T-185).
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

        // The draft never appears; total counts scannable rows only.
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

    /// <summary>
    /// The load-bearing one (P1T-179, P1T-185). Legitimate interest is not among the three
    /// Art. 22(2) exceptions, so a row on it has no route to automated decision-making at all — and
    /// the model call is the processing, so it cannot be scored "without persisting" either. The
    /// product consequence is stated rather than discovered: an unclaimed bench member is not
    /// scanned, and therefore not considered.
    /// </summary>
    [Fact]
    public async Task A_row_with_no_art22_route_is_never_enumerated()
    {
        using var factory = McpTestHost.CreateFactory(nameof(A_row_with_no_art22_route_is_never_enumerated));
        await SeedAsync(factory, activeCount: 1, draftCount: 0);
        var unclaimed = await SeedUnclaimedAsync(factory);
        await using var client = await McpTestHost.ConnectAsync(factory);

        var page = McpTestHost.Text(await client.CallToolAsync("roster_digest_list",
            new Dictionary<string, object?>()));

        page.Should().Contain("\"total\":1", "only the claimed row has a route")
            .And.NotContain(unclaimed.ToString())
            .And.NotContain("Unclaimed Ulric");
    }

    /// <summary>
    /// The other predicate on the same seam: somebody who paused themselves is not available for
    /// work, so they are not enumerated either — even though they keep <c>Status = Active</c> and
    /// their basis still carries a route.
    /// </summary>
    [Fact]
    public async Task A_paused_expert_is_never_enumerated()
    {
        using var factory = McpTestHost.CreateFactory(nameof(A_paused_expert_is_never_enumerated));
        var ids = await SeedAsync(factory, activeCount: 2, draftCount: 0);
        await PauseAsync(factory, ids[0]);
        await using var client = await McpTestHost.ConnectAsync(factory);

        var page = McpTestHost.Text(await client.CallToolAsync("roster_digest_list",
            new Dictionary<string, object?>()));

        page.Should().Contain("\"total\":1").And.NotContain(ids[0].ToString());
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
            // Claimed, so the scan may enumerate them: the digest page is the Roster Scan's
            // candidate list, and a row on legitimate interest has no Art. 22(2) route (P1T-185).
            expert.ProcessingRecords.Add(ProcessingRecord.For(
                expert.Id, 1, ProcessingOrigin.SelfRegistered, TransparencyNotice.CurrentVersion,
                "Registered and asked to be considered for work.", DateTimeOffset.UtcNow));
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

    /// <summary>An ordinary bench row nobody has claimed: Active, published, and on legitimate
    /// interest — exactly what the seeders and every staff-created row produce.</summary>
    private static async Task<Guid> SeedUnclaimedAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Unclaimed",
            LastName = "Ulric",
            Title = "Engineer",
            Email = "unclaimed@example.com",
            Status = ExpertStatus.Active,
            Summary = "Veteran stream wrangler across payments platforms.",
        };
        expert.ProcessingRecords.Add(ProcessingRecord.For(
            expert.Id, 1, ProcessingOrigin.StaffCreated, null,
            "Added to the bench by a Service Manager.", DateTimeOffset.UtcNow));

        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    private static async Task PauseAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Experts.Single(e => e.Id == expertId).HiddenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
