using CvManager.Application.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CvManager.Mcp.Tests;

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

    [Fact]
    public async Task Calling_with_theme_passes_it_through_and_returns_the_themed_result()
    {
        var stub = new StubExemplarSearch(new ExemplarSearchResult(
            [], new ThemeExemplars("cost reduction", [new StyleExemplar("Cut [company] spend by 30%.", 0.88)])));

        using var factory = McpTestHost
            .CreateFactory(nameof(Calling_with_theme_passes_it_through_and_returns_the_themed_result))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExemplarSearchService>();
                services.AddScoped<IExemplarSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("style_exemplar_search", new Dictionary<string, object?>
        {
            ["theme"] = "cost reduction",
        });

        result.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(result);
        text.Should().Contain("cost reduction").And.Contain("Cut [company] spend by 30%.");

        stub.LastAchievementIds.Should().BeNull();
        stub.LastTheme.Should().Be("cost reduction");
    }

    [Fact]
    public async Task Neither_achievementIds_nor_theme_is_a_validation_error()
    {
        var stub = new StubExemplarSearch(
            ExemplarSearchResult.Empty, throwOnBothOrNeither: true);

        using var factory = McpTestHost.CreateFactory(nameof(Neither_achievementIds_nor_theme_is_a_validation_error))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExemplarSearchService>();
                services.AddScoped<IExemplarSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("style_exemplar_search", new Dictionary<string, object?>());

        result.IsError.Should().Be(true);
        McpTestHost.Text(result).Should().Contain("validation");
    }

    private sealed class StubExemplarSearch : IExemplarSearchService
    {
        private readonly ExemplarSearchResult _result;
        private readonly bool _throwOnBothOrNeither;

        public StubExemplarSearch(ExemplarSearchResult result, bool throwOnBothOrNeither = false)
        {
            _result = result;
            _throwOnBothOrNeither = throwOnBothOrNeither;
        }

        public IReadOnlyList<Guid>? LastAchievementIds { get; private set; }
        public string? LastTheme { get; private set; }
        public int? LastTopKPerBullet { get; private set; }

        public Task<ExemplarSearchResult> SearchAsync(
            IReadOnlyList<Guid>? achievementIds,
            string? theme = null,
            int? topKPerBullet = null,
            CancellationToken ct = default)
        {
            var hasIds = achievementIds is { Count: > 0 };
            var hasTheme = !string.IsNullOrWhiteSpace(theme);
            if (_throwOnBothOrNeither && hasIds == hasTheme)
            {
                throw new FluentValidation.ValidationException(
                    "Provide either achievementIds or theme.");
            }

            LastAchievementIds = achievementIds;
            LastTheme = theme;
            LastTopKPerBullet = topKPerBullet;
            return Task.FromResult(_result);
        }
    }
}
