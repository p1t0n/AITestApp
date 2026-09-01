using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Live end-to-end smoke test for the staffing pipeline: hits the real chat model + the real MCP
/// server through the Agents SSE endpoint (P1T-76) and consumes the event stream. Excluded from
/// the default test run (mirrors the other live smokes). Run on demand with:
/// <c>dotnet test --filter "Category=live"</c>.
///
/// Preconditions (see README "Run it"): a Gemini API key in the <c>GEMINI_API_KEY</c> env var,
/// and a running MCP server + Keycloak with the seed data, reachable at the configured URLs.
/// </summary>
[Trait("Category", "live")]
public class StaffingLiveSmokeTests
{
    [SkippableFact]
    public async Task Streams_step_events_then_a_staffing_report_end_to_end()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live smoke test needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing",
            new
            {
                jobDescription = "Senior backend engineer for a payments platform: event streaming, " +
                                 "cloud infrastructure, and experience leading small teams.",
                matchTop = 2,
            });

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var frames = await response.ReadAllSseFramesAsync();

        // Ordered progress: the stream opens with the shortlist starting, every non-terminal
        // frame is a step transition, and stages appear in pipeline order.
        frames[0].Event.Should().Be("step");
        frames[0].Json.GetProperty("stage").GetString().Should().Be("shortlist");
        frames[0].Json.GetProperty("status").GetString().Should().Be("started");
        frames.Take(frames.Count - 1).Should().OnlyContain(f => f.Event == "step" || f.Event == "stepFailed");
        var stages = frames.Take(frames.Count - 1)
            .Select(f => f.Json.GetProperty("stage").GetString()!)
            .Distinct()
            .ToList();
        string[] pipelineOrder = ["shortlist", "match", "narrative"];
        stages.Should().Equal(pipelineOrder.Where(stages.Contains));

        // Terminal report: the same pinned contract the one-shot endpoint used to return.
        frames[^1].Event.Should().Be("report");
        var body = frames[^1].Json;
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
