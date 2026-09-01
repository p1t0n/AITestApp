using ExpertToJob.Application.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Tests the roster_semantic_search MCP tool wiring — exposure, scope, and param binding —
/// end-to-end over the MCP transport with a stubbed <see cref="ISemanticSearchService"/>. The real
/// pgvector ranking is covered by <see cref="SemanticSearchServiceTests"/>.
/// </summary>
public class RosterSearchToolsTests
{
    [Fact]
    public async Task Tool_is_exposed_under_the_read_scope()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Tool_is_exposed_under_the_read_scope));
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var tool = (await client.ListToolsAsync()).SingleOrDefault(t => t.Name == "roster_semantic_search");

        tool.Should().NotBeNull();
        tool!.Description.Should().Contain("semantic");
    }

    [Fact]
    public async Task Calling_the_tool_returns_ranked_employees_with_snippets()
    {
        var stub = new StubSearch(new SemanticSearchResult(
        [
            new SemanticSearchHit(
                Guid.NewGuid(), "Ada Lovelace", "Payments Lead", 0.87,
                ["Payments Lead @ BankCo (2019-03–present)\nLed the fintech payments rewrite."]),
        ]));

        using var factory = McpTestHost.CreateFactory(nameof(Calling_the_tool_returns_ranked_employees_with_snippets))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISemanticSearchService>();
                services.AddScoped<ISemanticSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("roster_semantic_search", new Dictionary<string, object?>
        {
            ["query"] = "fintech payments leaders",
            ["location"] = "London",
            ["topK"] = 3,
        });

        result.IsError.Should().NotBe(true);
        var text = McpTestHost.Text(result);
        text.Should().Contain("Ada Lovelace").And.Contain("fintech payments rewrite");

        // The tool bound the query + filters through to the service.
        stub.LastQuery.Should().Be("fintech payments leaders");
        stub.LastFilters.Should().NotBeNull();
        stub.LastFilters!.Location.Should().Be("London");
        stub.LastTopK.Should().Be(3);
    }

    [Fact]
    public async Task Calling_without_filters_passes_null_filters()
    {
        var stub = new StubSearch(SemanticSearchResult.Empty);

        using var factory = McpTestHost.CreateFactory(nameof(Calling_without_filters_passes_null_filters))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISemanticSearchService>();
                services.AddScoped<ISemanticSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        await client.CallToolAsync("roster_semantic_search", new Dictionary<string, object?>
        {
            ["query"] = "who has led incident response",
        });

        stub.LastQuery.Should().Be("who has led incident response");
        stub.LastFilters.Should().BeNull();
    }

    private sealed class StubSearch : ISemanticSearchService
    {
        private readonly SemanticSearchResult _result;

        public StubSearch(SemanticSearchResult result) => _result = result;

        public string? LastQuery { get; private set; }
        public SemanticSearchFilters? LastFilters { get; private set; }
        public int? LastTopK { get; private set; }

        public Task<SemanticSearchResult> SearchAsync(
            string query, SemanticSearchFilters? filters = null, int? topK = null, CancellationToken ct = default)
        {
            LastQuery = query;
            LastFilters = filters;
            LastTopK = topK;
            return Task.FromResult(_result);
        }
    }
}
