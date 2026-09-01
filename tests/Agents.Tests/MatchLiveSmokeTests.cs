using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test for the Match agent: hits the real chat model + the real MCP server
/// (authenticating as the agent-match Keycloak client) through the Agents HTTP endpoint. Excluded
/// from the default run. Run on demand: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions: a Gemini API key in <c>GEMINI_API_KEY</c>, and a running MCP server + Keycloak
/// (with the agent-match client) reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class MatchLiveSmokeTests
{
    [SkippableFact]
    public async Task Assesses_fit_end_to_end()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live smoke test needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        // The agents endpoints all RequireAuthorization; the original unauthenticated client
        // started 401ing when auth landed and went unnoticed (live smokes are opt-in).
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match",
            new
            {
                expertId = Guid.NewGuid(),
                jobDescription = "Senior backend engineer: .NET, PostgreSQL, distributed systems.",
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
