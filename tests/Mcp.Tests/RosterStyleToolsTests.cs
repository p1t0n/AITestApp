using EmployeeManager.Application.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// Tests the style_exemplar_search MCP tool wiring — exposure, scope, and param binding —
/// end-to-end over the MCP transport with a stubbed <see cref="IExemplarSearchService"/>. The
/// real pgvector retrieval + anonymization is covered by <see cref="ExemplarSearchServiceTests"/>.
/// </summary>
public class RosterStyleToolsTests
{
    [Fact]
    public async Task Tool_is_exposed_under_the_read_scope()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Tool_is_exposed_under_the_read_scope) + "_style");
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var tool = (await client.ListToolsAsync()).SingleOrDefault(t => t.Name == "style_exemplar_search");

        tool.Should().NotBeNull();
        tool!.Description.Should().Contain("anonymized");
    }

    [Fact]
    public async Task Calling_the_tool_returns_per_bullet_anonymized_exemplars()
    {
        var bulletId = Guid.NewGuid();
        var otherBulletId = Guid.NewGuid();
        var stub = new StubExemplarSearch(new ExemplarSearchResult(
        [
            new BulletExemplars(bulletId,
            [
                new StyleExemplar("Cut [company] deploy time by 60% across teams.", 0.91),
                new StyleExemplar("Scaled ingestion to 5M events daily for [name].", 0.84),
            ]),
            new BulletExemplars(otherBulletId, []),
        ]));

        using var factory = McpTestHost.CreateFactory(nameof(Calling_the_tool_returns_per_bullet_anonymized_exemplars))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExemplarSearchService>();
                services.AddScoped<IExemplarSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("style_exemplar_search", new Dictionary<string, object?>
        {
            ["achievementIds"] = new[] { bulletId.ToString(), otherBulletId.ToString() },
            ["topKPerBullet"] = 3,
        });

        result.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(result);
        text.Should().Contain(bulletId.ToString())
            .And.Contain("Cut [company] deploy time by 60% across teams.")
            .And.Contain("0.91")
            .And.Contain(otherBulletId.ToString());

        // The tool bound the Guid array and the optional int through to the service.
        stub.LastAchievementIds.Should().Equal(bulletId, otherBulletId);
        stub.LastTopKPerBullet.Should().Be(3);
    }

    [Fact]
    public async Task Calling_without_topK_passes_null_through()
    {
        var stub = new StubExemplarSearch(ExemplarSearchResult.Empty);

        using var factory = McpTestHost.CreateFactory(nameof(Calling_without_topK_passes_null_through) + "_style")
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExemplarSearchService>();
                services.AddScoped<IExemplarSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var bulletId = Guid.NewGuid();
        await client.CallToolAsync("style_exemplar_search", new Dictionary<string, object?>
        {
            ["achievementIds"] = new[] { bulletId.ToString() },
        });

        stub.LastAchievementIds.Should().Equal(bulletId);
        stub.LastTopKPerBullet.Should().BeNull();
    }

    private sealed class StubExemplarSearch : IExemplarSearchService
    {
        private readonly ExemplarSearchResult _result;

        public StubExemplarSearch(ExemplarSearchResult result) => _result = result;

        public IReadOnlyList<Guid>? LastAchievementIds { get; private set; }
        public int? LastTopKPerBullet { get; private set; }

        public Task<ExemplarSearchResult> SearchAsync(
            IReadOnlyList<Guid> achievementIds, int? topKPerBullet = null, CancellationToken ct = default)
        {
            LastAchievementIds = achievementIds;
            LastTopKPerBullet = topKPerBullet;
            return Task.FromResult(_result);
        }
    }
}
