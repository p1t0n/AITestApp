using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CvManager.Agents.Agents;
using CvManager.Agents.Staffing;
using CvManager.Agents.Tests.Fakes;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CvManager.Agents.Tests;

/// <summary>
/// The approver drill-in (P1T-134): GET /agents/staffing/proposals/{id} serves the proposal
/// metadata plus the full persisted handoff package, so a human can decide without re-running
/// anything. Includes the sufficiency gate — the drill-in's report node must expose every wire
/// StaffingReport field, permanently.
/// </summary>
public class StaffingProposalDrillInTests
{
    private static readonly Guid AdaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GraceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ShortlistCandidateItem Candidate(Guid id, string name) => new(
        id,
        name,
        "Platform Lead",
        0.91,
        new ShortlistCoverage(2, 3),
        [
            new ShortlistRequirementItem("event streaming with Kafka", true, "Built Kafka pipelines."),
            new ShortlistRequirementItem("Kubernetes operations", true, "Ran K8s clusters."),
            new ShortlistRequirementItem("team leadership", false, null),
        ],
        "Strong Kafka and K8s evidence.");

    /// <summary>The shortlist outcome carries a populated extraction, so the persisted report has
    /// every optional field present — the sufficiency gate must see them all on the wire.</summary>
    private static ShortlistRunOutcome ShortlistOutcome()
    {
        var response = new ShortlistResponse(
            ["event streaming with Kafka", "Kubernetes operations", "team leadership"],
            [Candidate(AdaId, "Ada Lovelace"), Candidate(GraceId, "Grace Hopper")]);
        return new ShortlistRunOutcome(
            "shortlist",
            new AgentReply("[]", 100, 20, 120, ModelId: "gemini-3.5-flash-lite"),
            response with
            {
                Extraction = new JdRequirements(
                    [new JdRequirement(
                        "event streaming with Kafka", RequirementKind.Skill, RequirementPriority.MustHave,
                        null, "Kafka", false)],
                    JdSeniority.Senior, null, []),
            },
            FaultDetail: null,
            ExtractionReply: new AgentReply("{}", 40, 10, 50));
    }

    private static FakeMatchRunService MatchOk() => new((id, _) => Task.FromResult(new MatchRunOutcome(
        "match",
        $"Gap analysis for {id}.",
        new AgentReply("answer", 200, 50, 250),
        Score: 78,
        Band: "Strong")));

    private static FakeChatClient NarrativeChat() => new(() => new ChatResponse(new ChatMessage(
        ChatRole.Assistant,
        $$"""
          {"rationales":[{"employeeId":"{{AdaId}}","rationale":"Best coverage."},{"employeeId":"{{GraceId}}","rationale":"Solid depth."}],
           "recommendation":{"employeeId":"{{AdaId}}","narrative":"Ada is the strongest fit."} }
          """))
    {
        Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 15, TotalTokenCount = 45 },
    });

    private static WebApplicationFactory<Program> FakedHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IShortlistRunService>(new FakeShortlistRunService(ShortlistOutcome()));
                s.AddSingleton<IMatchRunService>(MatchOk());
                s.AddSingleton<IChatClient>(NarrativeChat());
                s.AddSingleton(new StaffingThrottle(1));
                s.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
                    .IDbContextOptionsConfiguration<AppDbContext>));
                var dbName = $"proposal-drillin-{Guid.NewGuid()}";
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            }));

    /// <summary>Runs one staffing SSE round and returns the created proposal's id.</summary>
    private static async Task<Guid> RunOneProposalAsync(HttpClient client)
    {
        using var response = await client.PostSseAsync(
            "/agents/staffing",
            new { jobDescription = "Platform engineer: Kafka, Kubernetes, leadership.", matchTop = 2 });
        var frames = await response.ReadAllSseFramesAsync();
        frames[^1].Event.Should().Be("report");
        return frames[^1].Json.GetProperty("proposalId").GetGuid();
    }

    [Fact]
    public async Task Drill_in_serves_the_metadata_and_the_full_package()
    {
        using var factory = FakedHost();
        var userId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient(userId);
        var proposalId = await RunOneProposalAsync(client);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/agents/staffing/proposals/{proposalId}");

        // The inbox metadata rides along unchanged.
        detail.GetProperty("id").GetGuid().Should().Be(proposalId);
        detail.GetProperty("status").GetString().Should().Be("pending");
        detail.GetProperty("candidates").GetArrayLength().Should().Be(2);

        var package = detail.GetProperty("package");
        package.ValueKind.Should().Be(JsonValueKind.Object);

        // The full report: evidence, match markdown, recommendation narrative, extraction — and
        // its own proposal id, stamped at creation to match what the requester's SSE report shows.
        var report = package.GetProperty("report");
        report.GetProperty("proposalId").GetGuid().Should().Be(proposalId);
        report.GetProperty("requirements").GetArrayLength().Should().Be(3);
        var first = report.GetProperty("candidates")[0];
        first.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        first.GetProperty("shortlist").GetProperty("requirements").GetArrayLength().Should().Be(3);
        first.GetProperty("match").GetProperty("answer").GetString().Should().Contain("Gap analysis");
        first.GetProperty("rationale").GetString().Should().Be("Best coverage.");
        report.GetProperty("recommendation").GetProperty("narrative").GetString()
            .Should().Be("Ada is the strongest fit.");
        report.GetProperty("extraction").GetProperty("requirements").GetArrayLength().Should().Be(1);

        // Provenance and slices: who ran it, which identities acted, what it cost.
        package.GetProperty("provenance").GetProperty("callerUserId").GetGuid().Should().Be(userId);
        var slices = package.GetProperty("slices").EnumerateArray().ToList();
        slices.Select(s => s.GetProperty("stage").GetString()).Should().Equal(
            "jd-extraction", "shortlist", "match", "match", "narrative");
        slices.Should().OnlyContain(s => s.GetProperty("status").GetString() == "completed");
        slices[1].GetProperty("inputTokens").GetInt64().Should().Be(100);
        package.GetProperty("degradations").GetArrayLength().Should().Be(0);
    }

    /// <summary>The sufficiency gate (P1T-109 acceptance): the approver-visible package exposes
    /// every wire StaffingReport field. The fixture populates every optional field, so a report
    /// field missing from the drill-in JSON — today's or a future addition the persisted package
    /// doesn't carry — fails here, permanently.</summary>
    [Fact]
    public async Task The_drill_in_package_report_exposes_every_wire_report_field()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();
        var proposalId = await RunOneProposalAsync(client);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/agents/staffing/proposals/{proposalId}");
        var reportKeys = detail.GetProperty("package").GetProperty("report")
            .EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var property in typeof(StaffingReport).GetProperties())
        {
            var wireName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            reportKeys.Should().Contain(wireName,
                $"the drill-in must expose StaffingReport.{property.Name} to the approver");
        }
    }

    [Fact]
    public async Task A_pre_package_row_returns_its_metadata_with_a_null_package()
    {
        using var factory = FakedHost();
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.StaffingProposals.Add(new CvManager.Domain.Entities.StaffingProposal
            {
                Id = id,
                JobDescription = "Legacy JD",
                Status = CvManager.Domain.Entities.StaffingProposalStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                PackageJson = null,
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/agents/staffing/proposals/{id}");

        detail.GetProperty("jobDescription").GetString().Should().Be("Legacy JD");
        detail.GetProperty("package").ValueKind.Should().Be(
            JsonValueKind.Null, "pre-migration rows have no package and say so honestly");
    }

    [Fact]
    public async Task An_unknown_proposal_id_returns_404()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/agents/staffing/proposals/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        using var factory = FakedHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/agents/staffing/proposals/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
