using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CvManager.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test: hits the real chat model + the real MCP server through the
/// Agents HTTP endpoint. Excluded from the default test run (mirrors the Mcp Keycloak e2e
/// convention). Run on demand with: <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): a GitHub Models PAT in the <c>GITHUB_TOKEN</c> env var,
/// and a running MCP server + Keycloak with the seed data, reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class ShortlistLiveSmokeTests
{
    [SkippableFact]
    public async Task Shortlists_candidates_for_a_job_description_end_to_end()
    {
        // Skip (don't fail) when credentials are absent — this test only runs when opted into,
        // with a real PAT and the MCP server + Keycloak up. So it stays green in a plain
        // "run all tests" from the IDE.
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
            "Live smoke test needs a GitHub Models PAT in GITHUB_TOKEN (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist",
            new
            {
                jobDescription = "Senior backend engineer for a payments platform: event streaming, " +
                                 "cloud infrastructure, and experience leading small teams.",
                topK = 5,
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requirements").GetArrayLength().Should().BeGreaterThan(0);
        body.TryGetProperty("candidates", out var candidates).Should().BeTrue();
        foreach (var candidate in candidates.EnumerateArray())
        {
            candidate.GetProperty("rationale").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
