using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Live end-to-end smoke for Roster Scan (P1T-126): submits a real scan through the HTTP
/// surface (real chat model, real MCP digest sweeps through the agent-roster-scan identity,
/// real Postgres job rows) and polls it to a terminal state. Excluded from the default run:
/// <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): GEMINI_API_KEY, and a running MCP server + Keycloak
/// (with the agent-roster-scan client — recreate the Keycloak container if the realm predates
/// P1T-124) with seeded employees.
/// </summary>
[Trait("Category", "live")]
public class RosterScanLiveSmokeTests
{
    [SkippableFact]
    public async Task Scans_the_roster_end_to_end_with_honest_ranked_results()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live smoke needs GEMINI_API_KEY (and a running MCP server + Keycloak with seed data).");

        using var factory = new WebApplicationFactory<Program>();

        // The scan durably persists RequestedByUserId (a real Users FK), so unlike the metering-
        // only smokes this one needs an actual user behind its token.
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExpertToJob.Infrastructure.Persistence.AppDbContext>();
            db.Users.Add(new ExpertToJob.Domain.Entities.User
            {
                Id = userId,
                Email = $"roster-scan-smoke-{userId:N}@example.com",
                ControlWordHash = "smoke",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(userId);

        var submitted = await client.PostAsJsonAsync("/agents/roster-scan", new
        {
            jobDescription = "Senior backend engineer: event streaming (Kafka), cloud " +
                             "infrastructure, and experience leading small teams.",
        });
        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();
        var estimate = accepted.GetProperty("estimate");
        estimate.GetProperty("candidates").GetInt32().Should().BeGreaterThan(0, "the roster is seeded");
        estimate.GetProperty("rpdBudget").GetInt32().Should().BeGreaterThan(0);

        // Poll to a terminal state. On the 45-employee dev roster this is ~5 chunk calls; the
        // budget covers a 500-employee roster's 50 calls under RPM pacing too.
        var deadline = DateTime.UtcNow.AddMinutes(10);
        JsonElement job = default;
        while (DateTime.UtcNow < deadline)
        {
            job = await client.GetFromJsonAsync<JsonElement>($"/agents/roster-scan/{jobId}");
            var state = job.GetProperty("state").GetString();
            if (state is "completed" or "failed")
            {
                break;
            }

            // paused(quota) mid-smoke means the day's budget is genuinely gone — an honest skip,
            // not a failure of the feature.
            Skip.If(state == "paused" && job.GetProperty("pauseReason").GetString() == "quota",
                "The free-tier quota window is exhausted; the scan paused honestly.");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        job.GetProperty("state").GetString().Should().Be("completed");
        var progress = job.GetProperty("progress");
        progress.GetProperty("pending").GetInt32().Should().Be(0);
        progress.GetProperty("settled").GetInt32().Should().Be(progress.GetProperty("total").GetInt32());

        var candidates = job.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Should().NotBeEmpty();

        // Honest shapes: scored rows either carry a 0-100 score + band, or scorable:false with
        // both omitted — never an invented number.
        var scoredWithNumbers = new List<int>();
        foreach (var candidate in candidates.Where(c => c.GetProperty("status").GetString() == "scored"))
        {
            if (candidate.TryGetProperty("score", out var score))
            {
                score.GetInt32().Should().BeInRange(0, 100);
                candidate.GetProperty("band").GetString().Should().NotBeNullOrWhiteSpace();
                scoredWithNumbers.Add(score.GetInt32());
            }
            else
            {
                candidate.GetProperty("scorable").GetBoolean().Should().BeFalse(
                    "a scored row without a number must be an honest not-scorable");
            }
        }

        scoredWithNumbers.Should().NotBeEmpty("a Kafka JD over the seeded roster scores someone");
        scoredWithNumbers.Should().BeInDescendingOrder("the polling contract ranks scored rows by score");
    }
}
