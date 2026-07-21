using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test for the staffing pipeline: hits the real chat model + the real MCP
/// server through the Agents HTTP endpoint. Excluded from the default test run (mirrors the other
/// live smokes). Run on demand with: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): a GitHub Models PAT in the <c>GITHUB_TOKEN</c> env var,
/// and a running MCP server + Keycloak with the seed data, reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class StaffingLiveSmokeTests
{
    [SkippableFact]
    public async Task Produces_a_staffing_report_end_to_end()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
            "Live smoke test needs a GitHub Models PAT in GITHUB_TOKEN (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/staffing",
            new
            {
                jobDescription = "Senior backend engineer for a payments platform: event streaming, " +
                                 "cloud infrastructure, and experience leading small teams.",
                matchTop = 2,
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("requirements").GetArrayLength().Should().BeGreaterThan(0);
        var candidates = body.GetProperty("candidates");
        candidates.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var candidate in candidates.EnumerateArray())
        {
            candidate.GetProperty("employeeId").GetGuid().Should().NotBeEmpty();
            candidate.GetProperty("rationale").GetString().Should().NotBeNullOrWhiteSpace();
            candidate.GetProperty("match").GetProperty("status").GetString()
                .Should().BeOneOf("completed", "failed", "skipped");
        }

        // A healthy live run should not be degraded; if it is, the notes explain why (cap, 429s).
        if (!body.GetProperty("degraded").GetBoolean())
        {
            body.GetProperty("recommendation").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }
}
