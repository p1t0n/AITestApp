using System.Text.Json;
using CvManager.CostFloors;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace CvManager.Mcp.Tests.CostFloors;

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
    // pgvector, not stock postgres: the migrations create the `vector` extension for the RAG store.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory = null!;
    private Guid _employeeId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = McpTestHost.CreateFactoryWithPostgres(_postgres.GetConnectionString());

        using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // The same roster shape the 2026-08-30 measurement ran on: the first 45 dataset employees
        // over the full 79-skill catalog. A different count would make the ceilings incomparable.
        await DemoRosterSeeder.SeedAsync(
            db, DemoRosterSeeder.LoadCommittedDataset(), CvManager.CostFloors.CostFloors.DemoRosterEmployees);

        // A deterministic subject for the per-employee tools: lowest email wins, so the ceilings
        // measure the same person on every run.
        _employeeId = await db.Employees.OrderBy(e => e.Email).Select(e => e.Id).FirstAsync();
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
            ("skill_list", []),
            ("employee_list", []),
            ("category_list", []),
            ("category_tree", []),
            ("roster_digest_list", []),
            ("employee_get", new() { ["id"] = _employeeId }),
            ("cv_get", new() { ["employeeId"] = _employeeId }),
            ("availability_list", new() { ["employeeId"] = _employeeId }),
        };

        using var _ = new AssertionScope();
        foreach (var (tool, args) in calls)
        {
            var result = await client.CallToolAsync(tool, args);
            var tokens = TokenEstimate.Of(McpTestHost.Text(result));
            output.WriteLine($"{tool,-22} {tokens,6} tokens");

            tokens.Should().BeLessThanOrEqualTo(
                CvManager.CostFloors.CostFloors.ReadToolResultCeilings[tool],
                $"{tool}'s result is re-sent on every model call that follows it");
        }
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
            (CvManager.CostFloors.CostFloors.ReadToolResultCeilings.ContainsKey(tool)
             || CvManager.CostFloors.CostFloors.ModelBackedReadTools.Contains(tool))
                .Should().BeTrue(
                    $"{tool} needs a ratcheted result ceiling in CostFloors, or an entry in " +
                    "ModelBackedReadTools saying why it cannot have one");
        }
    }
}
