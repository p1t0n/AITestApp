using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/bench-report (P1T-104). Real host; the MCP tool source, chat
/// model, and DB are swapped for fakes/in-memory — the focus is the pinned contract (answer +
/// server-composed stats + notes) and the degrade ladder (roster fetch fault, model fault).
/// </summary>
public class BenchReportEndpointTests
{
    private const string EmployeesPayload =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","firstName":"Ada","lastName":"Lovelace","title":"Engineer","location":"London","email":"a@x.com","currentCapacityPercent":100,"status":"Active"},
         {"id":"22222222-2222-2222-2222-222222222222","firstName":"Grace","lastName":"Hopper","title":"Engineer","location":"Berlin","email":"g@x.com","currentCapacityPercent":0,"status":"Active"}]
        """;

    private static AIFunction EmployeeListTool() =>
        AIFunctionFactory.Create(() => EmployeesPayload, "employee_list");

    private static WebApplicationFactory<Program> FakedHost(
        IChatClient? chat = null, params AIFunction[] tools) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton(chat ?? new FakeChatClient(
                    () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "## Bench narrative"))));
                s.AddKeyedSingleton<ExpertToJob.Agents.Mcp.IMcpToolSource>(
                    "bench-report", (_, _) => new FakeToolSource(tools.Length > 0 ? tools : [EmployeeListTool()]));
                // Working in-memory DB so the proposals ledger is readable (and seedable) in tests.
                s.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
                    .IDbContextOptionsConfiguration<AppDbContext>));
                var dbName = $"bench-{Guid.NewGuid()}";
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            }));

    private static async Task SeedProposalAsync(WebApplicationFactory<Program> factory, string status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StaffingProposals.Add(new StaffingProposal
        {
            Id = Guid.NewGuid(),
            JobDescription = "Kafka platform engineer",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                new StaffingProposalCandidate
                {
                    Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Name = "Ada Lovelace", Rank = 1,
                },
            ],
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_narrative_with_server_composed_stats()
    {
        using var factory = FakedHost();
        await SeedProposalAsync(factory, StaffingProposalStatus.Pending);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/bench-report", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Be("## Bench narrative");

        var stats = body.GetProperty("stats");
        stats.GetProperty("activeEmployees").GetInt32().Should().Be(2);
        stats.GetProperty("fullyAvailable").GetInt32().Should().Be(1);
        stats.GetProperty("fullyBooked").GetInt32().Should().Be(1);
        stats.GetProperty("proposals").GetProperty("pending").GetInt32().Should().Be(1);
        stats.GetProperty("proposals").GetProperty("frequentCandidates")[0]
            .GetProperty("name").GetString().Should().Be("Ada Lovelace");
        body.GetProperty("notes").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Model_failure_ships_the_deterministic_fallback_with_a_note()
    {
        using var factory = FakedHost(chat: new FakeChatClient(
            () => throw new HttpRequestException("model down")));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/bench-report", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a model fault degrades, never 500s");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Contain("deterministic summary");
        body.GetProperty("stats").GetProperty("activeEmployees").GetInt32().Should().Be(2);
        body.GetProperty("notes").EnumerateArray().Select(n => n.GetString())
            .Should().ContainSingle(n => n!.Contains("Narrative unavailable"));
    }

    [Fact]
    public async Task Missing_roster_tool_degrades_to_ledger_only_stats_with_a_note()
    {
        using var factory = FakedHost(tools: AIFunctionFactory.Create(() => "x", "some_other_tool"));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/bench-report", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("stats").GetProperty("activeEmployees").GetInt32().Should().Be(0);
        body.GetProperty("notes").EnumerateArray().Select(n => n.GetString())
            .Should().ContainSingle(n => n!.Contains("Roster stats unavailable"));
    }
}
