using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The roster-scan endpoints (P1T-125) against the real host with fakes for the extractor, digest
/// source, and transport: the 202 + estimate contract (incl. the deliberate no-429-on-cap
/// decision), the polling payload once the background worker settles the job, and
/// requester-scoping on get/list.
/// </summary>
public class RosterScanEndpointTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeExtractor : IJdRequirementExtractor
    {
        public Task<JdExtractionOutcome> ExtractAsync(string jobDescription, CancellationToken ct = default) =>
            Task.FromResult(new JdExtractionOutcome(
                "jd-extraction", new AgentReply("{}", 10, 5, 15),
                new JdRequirements([], JdSeniority.Unspecified, null, []), null));
    }

    private sealed class FakeDigests : IRosterDigestSource
    {
        public Task<EmployeeDigestPage?> ListAsync(int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult<EmployeeDigestPage?>(page == 1
                ? new EmployeeDigestPage(1, pageSize, 2,
                [
                    new EmployeeDigest(Ada, "Ada Lovelace", "Engineer", "Kafka for 6 years."),
                    new EmployeeDigest(Grace, "Grace Hopper", "Admiral", "Compilers."),
                ])
                : new EmployeeDigestPage(page, pageSize, 2, []));
    }

    private sealed class FakeTransport : IScoringTransport
    {
        public Task<ScoredChunk> ScoreChunkAsync(
            string jobDescription, JdRequirements? extraction, IReadOnlyList<EmployeeDigest> chunk,
            CancellationToken ct = default) =>
            Task.FromResult(new ScoredChunk(
                chunk.Select((c, i) => i == 0
                    ? new ScoringCandidateResult(c.EmployeeId, ScoringCandidateStatus.Scored, 82, "Strong", "fit", true, null)
                    : new ScoringCandidateResult(c.EmployeeId, ScoringCandidateStatus.Scored, null, "Insufficient evidence", null, false, null))
                    .ToList(),
                new AgentReply("{}", 20, 10, 30)));
    }

    private static WebApplicationFactory<Program> FakedHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IJdRequirementExtractor>();
                s.AddSingleton<IJdRequirementExtractor>(new FakeExtractor());
                s.RemoveAll<IRosterDigestSource>();
                s.AddSingleton<IRosterDigestSource>(new FakeDigests());
                s.RemoveAll<IScoringTransport>();
                s.AddSingleton<IScoringTransport>(new FakeTransport());
                // Working in-memory DB so jobs persist (and users need not exist for the FK-less
                // provider) — the BenchReportEndpointTests pattern.
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<ExpertToJob.Infrastructure.Persistence.AppDbContext>));
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
                    .IDbContextOptionsConfiguration<ExpertToJob.Infrastructure.Persistence.AppDbContext>));
                var dbName = $"roster-scan-endpoints-{Guid.NewGuid()}";
                s.AddDbContext<ExpertToJob.Infrastructure.Persistence.AppDbContext>(o =>
                    Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions.UseInMemoryDatabase(o, dbName));
            }));

    private static async Task<JsonElement> PollUntilAsync(
        HttpClient client, Guid jobId, string state, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var response = await client.GetAsync($"/agents/roster-scan/{jobId}");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (body.GetProperty("state").GetString() == state || DateTime.UtcNow > deadline)
            {
                return body;
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Submit_returns_202_with_the_job_and_the_estimate()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/roster-scan", new { jobDescription = "Kafka engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
        var estimate = body.GetProperty("estimate");
        estimate.GetProperty("candidates").GetInt32().Should().Be(2, "the digest probe reports the roster total");
        estimate.GetProperty("calls").GetInt32().Should().Be(1, "2 candidates fit one default chunk");
        estimate.GetProperty("rpdBudget").GetInt32().Should().Be(500);
        response.Headers.Location!.ToString().Should().Contain($"/agents/roster-scan/{body.GetProperty("jobId").GetGuid()}");
    }

    [Fact]
    public async Task Submit_rejects_a_blank_job_description()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/roster-scan", new { jobDescription = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_accepts_even_when_the_cap_is_tripped_the_job_just_pauses()
    {
        // The deliberate contract decision: no 429 pre-check — a scan is a job, not a blocking
        // call. With the cap tripped the runner parks it as paused(cap) instead.
        var exceeded = new ExpertToJob.Agents.Usage.WindowUsage("daily", 50000, 50000, DateTimeOffset.UtcNow.AddHours(2));
        using var factory = FakedHost().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ExpertToJob.Agents.Usage.IUsageService>();
            s.AddSingleton<ExpertToJob.Agents.Usage.IUsageService>(new FakeUsageService(exceeded));
        }));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/roster-scan", new { jobDescription = "Kafka engineer." });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        var body = await PollUntilAsync(client, jobId, "paused");
        body.GetProperty("state").GetString().Should().Be("paused");
        body.GetProperty("pauseReason").GetString().Should().Be("cap");
    }

    [Fact]
    public async Task Polling_reaches_completed_with_ranked_honest_candidates()
    {
        using var factory = FakedHost();
        using var client = factory.CreateAuthenticatedClient();

        var submitted = await client.PostAsJsonAsync("/agents/roster-scan", new { jobDescription = "Kafka engineer." });
        var jobId = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();

        var body = await PollUntilAsync(client, jobId, "completed");

        body.GetProperty("state").GetString().Should().Be("completed");
        var progress = body.GetProperty("progress");
        progress.GetProperty("scored").GetInt32().Should().Be(2);
        progress.GetProperty("pending").GetInt32().Should().Be(0);

        var candidates = body.GetProperty("candidates");
        candidates.GetArrayLength().Should().Be(2);
        // Scored-by-score-desc: Ada (82) before the not-scorable row.
        candidates[0].GetProperty("employeeId").GetGuid().Should().Be(Ada);
        candidates[0].GetProperty("score").GetInt32().Should().Be(82);
        candidates[0].GetProperty("band").GetString().Should().Be("Strong");
        candidates[1].GetProperty("scorable").GetBoolean().Should().BeFalse("honest absence rides the wire");
        candidates[1].TryGetProperty("score", out _).Should().BeFalse("null score is omitted, not zeroed");
    }

    [Fact]
    public async Task Get_and_list_are_requester_scoped()
    {
        using var factory = FakedHost();
        using var owner = factory.CreateAuthenticatedClient();
        using var stranger = factory.CreateAuthenticatedClient();

        var submitted = await owner.PostAsJsonAsync("/agents/roster-scan", new { jobDescription = "Kafka engineer." });
        var jobId = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();

        (await owner.GetAsync($"/agents/roster-scan/{jobId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await stranger.GetAsync($"/agents/roster-scan/{jobId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ownerList = await owner.GetFromJsonAsync<JsonElement>("/agents/roster-scan");
        ownerList.EnumerateArray().Select(j => j.GetProperty("jobId").GetGuid()).Should().Contain(jobId);
        var strangerList = await stranger.GetFromJsonAsync<JsonElement>("/agents/roster-scan");
        strangerList.EnumerateArray().Should().BeEmpty();
    }
}
