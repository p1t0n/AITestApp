using CvManager.Agents.Agents;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CvManager.Agents.Tests.CostFloors;

/// <summary>
/// The live half of the Convergence floor (P1T-148) — the deterministic one in
/// <see cref="ConvergenceCostFloorTests"/> prices a path we DECLARE; this one measures the path the
/// model actually walks, on the reference question the whole cost chain was opened by.
///
/// <para>Excluded from the default run (<c>Category=live</c>): it needs a Gemini key and a running
/// MCP server + Keycloak with the seeded demo roster, exactly like
/// <see cref="RosterQaLiveSmokeTests"/>. It asserts real Gemini tokens, not
/// <c>TokenEstimate</c> ones — the two differ by roughly 2.4× on GUID-dense payloads, so the
/// ceilings here and there are denominated in different units and must not be compared.</para>
///
/// <para>Run it: <c>GEMINI_API_KEY=&lt;key&gt; dotnet test tests/Agents.Tests --filter "Category=live"</c>.</para>
/// </summary>
[Trait("Category", "live")]
public class RosterQaConvergenceLiveFloorTests(ITestOutputHelper output)
{
    /// <summary>The question in `manuals/agent-cost-budgets.md` §1.3, kept verbatim so a re-run is
    /// comparable to the 160,220-token trace and to the ~5,400 it cost on 2026-08-02.</summary>
    private const string ReferenceQuestion = "who knows react and lives in London";

    /// <summary>Real Gemini input tokens for the whole run — §3.1's Cost Floor for roster-qa.</summary>
    private const int InputTokenCeiling = 8_000;

    /// <summary>Model calls, the same ratchet the deterministic floor holds the declared path to.</summary>
    private const int IterationCeiling =
        CvManager.CostFloors.CostFloors.RosterQaConvergentRunIterationCeiling;

    [SkippableFact]
    public async Task The_reference_question_converges_inside_its_cost_floor()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live Convergence floor needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        var agent = factory.Services.GetServices<IChatAgent>().OfType<RosterQaAgent>().Single();

        // Straight at the agent rather than through the endpoint: AgentReply already carries the
        // iteration count and the tool sequence, and POST /agents/roster-qa returns neither.
        var reply = await agent.AskAsync(ReferenceQuestion);

        output.WriteLine(
            $"{reply.Iterations} model calls, {reply.InputTokens} in / {reply.OutputTokens} out, " +
            $"{reply.LatencyMs}ms — {reply.ToolSequence}");
        output.WriteLine(reply.Text);

        using var _ = new AssertionScope();
        reply.Iterations.Should().BeLessThanOrEqualTo(
            IterationCeiling, "the traced run took 10 to reach the same answer");
        reply.InputTokens.Should().BeLessThanOrEqualTo(
            InputTokenCeiling, "input was 99.6% of the 160,220-token regression");

        // Convergence bought with a worse answer is not Convergence. The seeded roster has people
        // in London and none of them know React, so the honest answer names London and says no.
        reply.Text.Should().NotBeNullOrWhiteSpace();
        reply.ToolSequence.Should().NotBeNullOrEmpty("an ungrounded answer is a Capture-Verify failure");
    }
}
