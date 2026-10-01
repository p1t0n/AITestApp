using ExpertToJob.Application.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

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
        var stub = new StubShortlist(new ShortlistMatches(
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
        var stub = new StubShortlist(ShortlistMatches.None);

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

    /// <summary>
    /// The wire guard (EXP-75). <c>roster_shortlist_search</c>'s result is a published tool
    /// contract, so its serialised shape is asserted literally rather than by property probes:
    /// an internal refactor behind <see cref="IShortlistSearchService"/> must not move a single
    /// byte of it. Both halves are pinned — a hit list, and the soft-fault form the agent reads
    /// to degrade (<c>ShortlistRunService</c> keys off exactly this <c>error</c> field).
    /// </summary>
    [Fact]
    public async Task The_serialised_tool_result_is_byte_for_byte_the_published_shape()
    {
        var stub = new StubShortlist(new ShortlistMatches(
        [
            new ShortlistCandidate(
                Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ada Lovelace", "Payments Lead",
                0.75, 1, 2,
                [
                    new ShortlistRequirementEvidence("kafka", true, "Ran the Kafka event backbone.", 0.82),
                    new ShortlistRequirementEvidence("cobol", false),
                ]),
        ]));

        using var factory = McpTestHost.CreateFactory(nameof(The_serialised_tool_result_is_byte_for_byte_the_published_shape))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IShortlistSearchService>();
                services.AddScoped<IShortlistSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("roster_shortlist_search", new Dictionary<string, object?>
        {
            ["requirements"] = new[] { "kafka", "cobol" },
        });

        McpTestHost.Text(result).Should().Be(
            """
            {"results":[{"expertId":"11111111-1111-1111-1111-111111111111","name":"Ada Lovelace","title":"Payments Lead","score":0.75,"matchedCount":1,"totalRequirements":2,"evidence":[{"requirement":"kafka","matched":true,"snippet":"Ran the Kafka event backbone.","similarity":0.82},{"requirement":"cobol","matched":false,"snippet":null,"similarity":null}]}],"error":null}
            """);
    }

    /// <summary>The fault half of the same contract: an empty list plus the error string, which is
    /// what lets the agent degrade instead of surfacing a transport failure.</summary>
    [Fact]
    public async Task The_serialised_fault_result_is_byte_for_byte_the_published_shape()
    {
        var stub = new StubShortlist(new ShortlistSearchFault("The semantic search backend is unavailable."));

        using var factory = McpTestHost.CreateFactory(nameof(The_serialised_fault_result_is_byte_for_byte_the_published_shape))
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IShortlistSearchService>();
                services.AddScoped<IShortlistSearchService>(_ => stub);
            }));

        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("roster_shortlist_search", new Dictionary<string, object?>
        {
            ["requirements"] = new[] { "kafka" },
        });

        McpTestHost.Text(result).Should().Be(
            """
            {"results":[],"error":"The semantic search backend is unavailable."}
            """);
    }

    private sealed class StubShortlist : IShortlistSearchService
    {
        private readonly ShortlistSearchOutcome _result;

        public StubShortlist(ShortlistSearchOutcome result) => _result = result;

        public IReadOnlyList<string>? LastRequirements { get; private set; }
        public SemanticSearchFilters? LastFilters { get; private set; }
        public int? LastTopK { get; private set; }

        public Task<ShortlistSearchOutcome> SearchAsync(
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
