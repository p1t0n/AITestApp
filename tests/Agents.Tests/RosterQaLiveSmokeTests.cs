using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test: hits the real chat model + the real MCP server through the
/// Agents HTTP endpoint. Excluded from the default test run (mirrors the Mcp Keycloak e2e
/// convention). Run on demand with: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): a Gemini API key in the <c>GEMINI_API_KEY</c> env var,
/// and a running MCP server + Keycloak with the seed data, reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class RosterQaLiveSmokeTests
{
    [SkippableFact]
    public async Task Answers_a_roster_question_end_to_end()
    {
        // Skip (don't fail) when credentials are absent — this test only runs when opted into,
        // with a real PAT and the MCP server + Keycloak up. So it stays green in a plain
        // "run all tests" from the IDE.
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live smoke test needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/roster-qa", new { question = "Which experts are in the roster?" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
