using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CvManager.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test for CV tailoring: hits the real chat model + the real MCP server
/// (authenticating as the agent-cv-tailoring Keycloak client) through the Agents HTTP endpoint.
/// Excluded from the default run. Run on demand: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions: a Gemini API key in <c>GEMINI_API_KEY</c>, and a running MCP server + Keycloak
/// (with the agent-cv-tailoring client) reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class CvTailoringLiveSmokeTests
{
    [SkippableFact]
    public async Task Tailors_a_cv_end_to_end()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live smoke test needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/cv-tailoring",
            new
            {
                employeeId = Guid.NewGuid(),
                jobDescription = "Senior backend engineer: .NET, PostgreSQL, distributed systems.",
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();

        // The hybrid contract: rewrites ride along (possibly empty — e.g. an unknown employee or
        // a degraded rewrite turn), and every entry the guard let through is fully populated.
        var rewrites = body.GetProperty("rewrites");
        rewrites.ValueKind.Should().Be(JsonValueKind.Array);
        foreach (var rewrite in rewrites.EnumerateArray())
        {
            rewrite.GetProperty("experienceId").GetGuid().Should().NotBeEmpty();
            rewrite.GetProperty("achievementId").GetGuid().Should().NotBeEmpty();
            rewrite.GetProperty("original").GetString().Should().NotBeNullOrWhiteSpace();
            rewrite.GetProperty("rewritten").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
