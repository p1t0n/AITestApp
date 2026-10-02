using System.Text.Json;
using ExpertToJob.CostFloors;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Mcp.Tests.CostFloors;

/// <summary>
/// The result half of the Cost Floors (P1T-144). A read tool's result is charged once when it is
/// fetched and again on every model call after it (Turn Amplification), so an unfiltered result is
/// the most expensive thing a tool surface can do: <c>skill_list</c> alone was 42% of a
/// 160,220-token roster-qa run.
///
/// <para>No model is involved. The tools run against the real seeded demo roster on real Postgres
/// and their payloads are measured with <see cref="TokenEstimate"/>, which is exactly why this
/// floor runs on every push where the live agent evals (<c>Category=eval</c>, opt-in, needs a key)
/// never could — that blind spot is how a 27× cost regression shipped green.</para>
/// </summary>
public sealed class ReadToolResultCostFloorTests(ITestOutputHelper output) : IAsyncLifetime
{
    /// <summary>The measurement day, as a clock the host can be handed.</summary>
    private sealed class PinnedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    // pgvector, not stock postgres: the migrations create the `vector` extension for the RAG store.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory = null!;
    private Guid _expertId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Pinned to the measurement date. The seeded roster's availability entries are dated and a
        // read tool resolves capacity against "today", so an unpinned clock makes this floor fail
        // on whatever day the calendar next crosses one of those dates — which is what it did
        // (P1T-199). The ceiling measures the payload; the date is not part of the measurement.
        _factory = McpTestHost.CreateFactoryWithPostgres(
            _postgres.GetConnectionString(), new PinnedClock(ExpertToJob.CostFloors.CostFloors.MeasuredOn));

        using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // The same roster shape the 2026-08-30 measurement ran on: the first 45 dataset experts
        // over the full 79-skill catalog. A different count would make the ceilings incomparable.
        await DemoRosterSeeder.SeedAsync(
            db, DemoRosterSeeder.LoadCommittedDataset(), ExpertToJob.CostFloors.CostFloors.DemoRosterExperts);

        // A deterministic subject for the per-expert tools: lowest email wins, so the ceilings
        // measure the same person on every run.
        _expertId = await db.Experts.OrderBy(e => e.Email).Select(e => e.Id).FirstAsync();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Every_model_free_read_tool_result_stays_under_its_ratcheted_ceiling()
    {
        await using var client = await McpTestHost.ConnectAsync(_factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var calls = new (string Tool, Dictionary<string, object?> Args)[]
        {
            // The call the traced run actually wanted: resolve "react" to a catalog skill id
            // before filtering a search by it. Before P1T-145 there was no way to ask for less
            // than all 79 skills, and that answer was re-sent on nine following model calls.
            ("skill_list", new() { ["nameContains"] = "React" }),
            ("expert_list", []),
            ("category_list", []),
            ("category_tree", []),
            ("roster_digest_list", []),
            ("expert_get", new() { ["id"] = _expertId }),
            ("cv_get", new() { ["expertId"] = _expertId }),
            ("availability_list", new() { ["expertId"] = _expertId }),
        };

        using var _ = new AssertionScope();
        foreach (var (tool, args) in calls)
        {
            var result = await client.CallToolAsync(tool, args);
            var tokens = TokenEstimate.Of(McpTestHost.Text(result));
            output.WriteLine($"{tool,-22} {tokens,6} tokens");

            tokens.Should().BeLessThanOrEqualTo(
                ExpertToJob.CostFloors.CostFloors.ReadToolResultCeilings[tool],
                $"{tool}'s result is re-sent on every model call that follows it");
        }
    }

    /// <summary>
    /// The filtered lookup above is the hot path, but the unfiltered sweep still exists and
    /// resume-ingestion still uses it — so it stays measured rather than becoming the unwatched
    /// half of the tool. This is also the guard on the page default: it is sized to hold the whole
    /// seeded catalog, and if the catalog outgrows it this ceiling is what says so.
    /// </summary>
    [Fact]
    public async Task Skill_list_without_a_filter_returns_one_bounded_page()
    {
        await using var client = await McpTestHost.ConnectAsync(_factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var result = await client.CallToolAsync("skill_list", new Dictionary<string, object?>());
        var text = McpTestHost.Text(result);
        var tokens = TokenEstimate.Of(text);
        output.WriteLine($"skill_list (unfiltered) {tokens,6} tokens");

        tokens.Should().BeLessThanOrEqualTo(ExpertToJob.CostFloors.CostFloors.SkillListUnfilteredPageCeiling);

        // One page, and the whole catalog fits in it: ResumeIngestionAgent loads the catalog with
        // a single unfiltered call and matches resume skills against what comes back.
        var page = JsonDocument.Parse(text).RootElement;
        page.GetProperty("total").GetInt32().Should()
            .BeLessThanOrEqualTo(page.GetProperty("items").GetArrayLength(),
                "the default page must still hold the whole catalog");
    }

    /// <summary>
    /// The other half of EXP-94's trade, measured rather than asserted. The unfiltered sweep above
    /// is what the Tool Result Budget refuses; this is the call the filter makes possible, and the
    /// gap between the two numbers is the whole argument for paying +132 tokens of schema on every
    /// iteration of every agent shown this tool.
    /// </summary>
    [Fact]
    public async Task Expert_list_filtered_by_location_is_a_fraction_of_the_whole_bench()
    {
        await using var client = await McpTestHost.ConnectAsync(_factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        // "Warsaw, Poland" is the seeded roster's own data — the same place the bug was reported
        // against, three of the 45 rather than 31 of the 505.
        var result = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["location"] = "warsaw" });
        var text = McpTestHost.Text(result);
        var tokens = TokenEstimate.Of(text);
        output.WriteLine($"expert_list (location=warsaw) {tokens,6} tokens");

        tokens.Should().BeLessThanOrEqualTo(
            ExpertToJob.CostFloors.CostFloors.ExpertListFilteredCeiling,
            "the filtered call is the one a count question should make");

        // And it is a real answer, not an empty one that happens to be cheap.
        var page = JsonDocument.Parse(text).RootElement;
        page.GetProperty("total").GetInt32().Should().Be(3);
        page.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    /// <summary>
    /// EXP-96's count-only call, measured. The filtered call above is cheap because the filter is
    /// narrow; this one is cheap no matter what is asked, because the rows never leave the
    /// database — which is what makes it the answer to "how many experts are on the roster in
    /// total?", the question the unfiltered sweep could not answer at all over 505 experts.
    /// </summary>
    [Fact]
    public async Task Expert_list_count_only_is_a_number_the_budget_can_never_refuse()
    {
        await using var client = await McpTestHost.ConnectAsync(_factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var result = await client.CallToolAsync(
            "expert_list", new Dictionary<string, object?> { ["countOnly"] = true });
        var text = McpTestHost.Text(result);
        var tokens = TokenEstimate.Of(text);
        output.WriteLine($"expert_list (countOnly) {tokens,6} tokens");

        tokens.Should().BeLessThanOrEqualTo(
            ExpertToJob.CostFloors.CostFloors.ExpertListCountOnlyCeiling,
            "a count must stay answerable at any roster size");

        // The number is the whole seeded roster, not a page of it — cheap because the rows stayed
        // in the database, not because the count was truncated.
        var page = JsonDocument.Parse(text).RootElement;
        page.GetProperty("total").GetInt32().Should().Be(ExpertToJob.CostFloors.CostFloors.DemoRosterExperts);
        page.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Every_read_tool_is_either_ratcheted_or_declared_model_backed()
    {
        await using var client = await McpTestHost.ConnectAsync(_factory, McpTestHost.MintToken(McpTestHost.ReadScope));

        var readTools = (await client.ListToolsAsync())
            .Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true)
            .Select(t => t.Name)
            .ToList();

        // The guard that makes this floor hold over time: a read tool added without a ceiling is a
        // tool whose cost nobody measured, which is precisely how the last regression shipped.
        using var _ = new AssertionScope();
        foreach (var tool in readTools)
        {
            (ExpertToJob.CostFloors.CostFloors.ReadToolResultCeilings.ContainsKey(tool)
             || ExpertToJob.CostFloors.CostFloors.ModelBackedReadTools.Contains(tool))
                .Should().BeTrue(
                    $"{tool} needs a ratcheted result ceiling in CostFloors, or an entry in " +
                    "ModelBackedReadTools saying why it cannot have one");
        }
    }
}
