using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test: hits the real chat model + the real MCP server through the
/// Agents HTTP endpoint. Excluded from the default test run (mirrors the Mcp Keycloak e2e
/// convention). Run on demand with: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): a GitHub Models PAT in the <c>GITHUB_TOKEN</c> env var,
/// and a running MCP server + Keycloak with the seed data, reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class RosterQaLiveSmokeTests
{
    [Fact]
    public async Task Answers_a_roster_question_end_to_end()
    {
        Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            .Should().NotBeNullOrWhiteSpace("the live smoke test needs a GitHub Models PAT in GITHUB_TOKEN");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/roster-qa", new { question = "Which employees are in the roster?" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
