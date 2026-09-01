using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Tests.Eval;
using ExpertToJob.CostFloors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests.CostFloors;

/// <summary>
/// The live half of the ingestion Convergence floor (P1T-155) — the sibling of
/// <see cref="RosterQaConvergenceLiveFloorTests"/>, and the confirmation
/// <see cref="IngestionRunCostFloorTests"/> structurally cannot give.
///
/// <para>That floor prices the shape we DECLARE: it scripts the tool calls and weighs what the
/// conversation costs. Both of P1T-155's changes are instruction rewrites, so the declared shape
/// is exactly the thing in question — a model free to ignore the Batching rule lands back on the
/// 24-call serial shape, and one that ignores step 1 dumps the catalog again, and the
/// deterministic floor stays green through either. Only a real run settles it.</para>
///
/// <para>Excluded from the default run (<c>Category=live</c>): it needs a Gemini key and a running
/// MCP server + Keycloak, exactly like <see cref="RosterQaLiveSmokeTests"/>. It asserts real
/// Gemini tokens, not <c>TokenEstimate</c> ones — the two differ by roughly 2.4× on GUID-dense
/// payloads, so the ceilings here and in <see cref="IngestionRunCost"/> are denominated in
/// different units and must not be compared.</para>
///
/// <para><b>It writes.</b> resume-ingestion is the one agent holding <c>mcp:write</c>, so a pass
/// leaves a real DRAFT expert behind — Torvald Emberwright, staged for the approval gate like
/// any other ingestion. That is the run being measured, not a side effect to design away: a
/// harness that stubbed the writes would be pricing a different loop.</para>
///
/// <para>Run it: <c>GEMINI_API_KEY=&lt;key&gt; dotnet test tests/Agents.Tests --filter "Category=live"</c>.</para>
/// </summary>
[Trait("Category", "live")]
public class IngestionConvergenceLiveFloorTests(ITestOutputHelper output)
{
    /// <summary>The reference resume the deterministic floor is measured over, so a live run is
    /// comparable to it call for call: the EASY fixture, everything already in the catalog.</summary>
    private static readonly ResumeFixture Reference =
        Fixtures.All.Single(f => f.Id == IngestionRunCost.ReferenceResumeId);

    /// <summary>
    /// Model calls a live run may take. The declared shape is <see cref="IngestionRunCost.BatchedIterations"/>
    /// (7); this allows half again for the retries the instructions permit, and still sits far
    /// under the 24 the serial shape needs. A run above it means the Batching rule did not land —
    /// which is the single thing this test exists to detect.
    /// </summary>
    private const int IterationCeiling = 11;

    /// <summary>
    /// Real Gemini input tokens for the whole run. The ledger's worst recorded ingestion was
    /// 155,668; the deterministic floor prices the declared shape at 31,247 ESTIMATED tokens, and
    /// real runs about 2.4× that on the GUID-dense payloads a write loop carries. 40,000 is the
    /// Runtime Budget, so this ceiling is set at it: the question this test answers is not "is it
    /// cheaper" — the deterministic floor answered that — but "does an ordinary resume now finish
    /// instead of degrading", and that is what <c>MaxInputTokens</c> decides.
    /// </summary>
    private const int InputTokenCeiling = 40_000;

    [SkippableFact]
    public async Task The_reference_resume_ingests_inside_its_runtime_budget()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live ingestion Convergence floor needs a Gemini API key in GEMINI_API_KEY (and a running MCP server + Keycloak).");

        using var factory = new WebApplicationFactory<Program>();
        var agent = factory.Services.GetRequiredService<ResumeIngestionAgent>();

        // Straight at the agent rather than through the endpoint: the outcome carries the
        // iteration count, the tool sequence and the Degradation, and POST /agents/ingest-resume
        // returns the run service's composed report instead.
        var outcome = await agent.IngestAsync(Reference.Text);
        var reply = outcome.Reply;

        output.WriteLine(
            $"{reply.Iterations} model calls, {reply.InputTokens} in / {reply.OutputTokens} out, " +
            $"{reply.LatencyMs}ms — {reply.ToolSequence}");
        output.WriteLine($"draft {outcome.ExpertId}, {outcome.ToolCalls.Count} write calls, " +
                         $"degradation: {reply.Degradation ?? "none"}");

        using var _ = new AssertionScope();

        // Convergence, in the two units that can disagree. Iterations is what the Batching rule
        // moves; input tokens is what the whole ticket is for, and the ceiling it must clear is
        // the production one rather than a CI ratchet.
        reply.Iterations.Should().BeLessThanOrEqualTo(
            IterationCeiling,
            $"the declared shape is {IngestionRunCost.BatchedIterations} calls and the serial one it " +
            "replaces was 24");
        reply.InputTokens.Should().BeLessThanOrEqualTo(
            InputTokenCeiling, "an ordinary resume must finish inside the Runtime Budget, not degrade at it");
        reply.Degradation.Should().BeNull(
            "a run that degrades leaves a half-populated draft behind the approval gate");

        // Cheapness bought with a worse draft is not Convergence, and on the one agent holding
        // mcp:write it is the expensive kind of wrong. The ground truth is the same one
        // ExtractionFidelityEvalTests holds the extractor to: every child actually written.
        var truth = Reference.Truth;
        outcome.ExpertId.Should().NotBeNull("the draft is the deliverable");
        Written(outcome, "language_add").Should().Be(truth.Languages.Count);
        Written(outcome, "expert_skill_add").Should().Be(truth.Skills.Count(s => s.InCatalog));
        Written(outcome, "qualification_add").Should().Be(truth.Qualifications.Count);
        Written(outcome, "experience_add").Should().Be(truth.Experiences.Count);
        outcome.ToolCalls.Where(c => !c.Succeeded).Should().BeEmpty(
            "the reference fixture needs no self-correction; a failure here is a batching artefact");
    }

    private static int Written(ResumeIngestionOutcome outcome, string tool) =>
        outcome.ToolCalls.Count(c => c.Tool == tool && c.Succeeded);
}
