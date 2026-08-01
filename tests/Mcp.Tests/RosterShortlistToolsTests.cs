using CvManager.Application.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CvManager.Mcp.Tests;

/// <summary>
/// Tests the roster_shortlist_search MCP tool wiring — exposure, scope, and param binding —
/// end-to-end over the MCP transport with a stubbed <see cref="IShortlistSearchService"/>. The
/// real pgvector coverage ranking is covered by <see cref="ShortlistSearchServiceTests"/>.
/// </summary>
public class RosterShortlistToolsTests
{
    [Fact]
    public async Task Tool_is_exposed_under_the_read_scope()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Tool_is_exposed_under_the_read_scope) + "_shortlist");
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var tool = (await client.ListToolsAsync()).SingleOrDefault(t => t.Name == "roster_shortlist_search");

        tool.Should().NotBeNull();
        tool!.Description.Should().Contain("requirement");
    }

    [Fact]
    public async Task Calling_the_tool_returns_coverage_ranked_candidates_with_evidence()
    {
        var stub = new StubShortlist(new ShortlistSearchResult(
        [
            new ShortlistCandidate(
                Guid.NewGuid(), "Ada Lovelace", "Payments Lead", 0.7841, 2, 3,
                [
                    new ShortlistRequirementEvidence("kafka", true, "Ran the Kafka event backbone.", 0.82),
                    new ShortlistRequirementEvidence("terraform", true, "Owned the Terraform estate.", 0.74),
                    new ShortlistRequirementEvidence("cobol", false),
                ]),
        ]));

        using var factory = McpTestHost.CreateFactory(nameof(Calling_the_tool_returns_coverage_ranked_candidates_with_evidence))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IShortlistSearchService>();
                services.AddScoped<IShortlistSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("roster_shortlist_search", new Dictionary<string, object?>
        {
            ["requirements"] = new[] { "kafka", "terraform", "cobol" },
            ["location"] = "London",
            ["minYears"] = 3,
            ["topK"] = 5,
        });

        result.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(result);
        text.Should().Contain("Ada Lovelace")
            .And.Contain("\"matchedCount\":2")
            .And.Contain("\"totalRequirements\":3")
            .And.Contain("Ran the Kafka event backbone.")
            .And.Contain("\"matched\":false");

        // The tool bound the requirements + filters through to the service.
        stub.LastRequirements.Should().Equal("kafka", "terraform", "cobol");
        stub.LastFilters.Should().NotBeNull();
        stub.LastFilters!.Location.Should().Be("London");
        stub.LastFilters.MinYears.Should().Be(3);
        stub.LastTopK.Should().Be(5);
    }

    [Fact]
    public async Task Calling_without_filters_passes_null_filters()
    {
        var stub = new StubShortlist(ShortlistSearchResult.Empty);

        using var factory = McpTestHost.CreateFactory(nameof(Calling_without_filters_passes_null_filters) + "_shortlist")
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IShortlistSearchService>();
                services.AddScoped<IShortlistSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        await client.CallToolAsync("roster_shortlist_search", new Dictionary<string, object?>
        {
            ["requirements"] = new[] { "kafka" },
        });

        stub.LastRequirements.Should().Equal("kafka");
        stub.LastFilters.Should().BeNull();
        stub.LastTopK.Should().BeNull();
    }

    private sealed class StubShortlist : IShortlistSearchService
    {
        private readonly ShortlistSearchResult _result;

        public StubShortlist(ShortlistSearchResult result) => _result = result;

        public IReadOnlyList<string>? LastRequirements { get; private set; }
        public SemanticSearchFilters? LastFilters { get; private set; }
        public int? LastTopK { get; private set; }

        public Task<ShortlistSearchResult> SearchAsync(
            IReadOnlyList<string> requirements, SemanticSearchFilters? filters = null, int? topK = null,
            CancellationToken ct = default)
        {
            LastRequirements = requirements;
            LastFilters = filters;
            LastTopK = topK;
            return Task.FromResult(_result);
        }
    }
}
