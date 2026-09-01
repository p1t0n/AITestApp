using ExpertToJob.Agents.Agents;
using ExpertToJob.ExtractionEval;
using FluentAssertions;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests.Eval;

/// <summary>
/// Live extraction-fidelity regression gate (P1T-119): runs the REAL
/// <see cref="JdRequirementExtractor"/> over the frozen golden JD set and asserts the committed
/// floors in <see cref="ExtractionEvalBaselines"/> — including the hard fabrication-rate=0 gate
/// (no invented must-haves, seniority, location, or years on silent slots). Same discipline as
/// the retrieval gate. Run: <c>GEMINI_API_KEY=&lt;key&gt; dotnet test --filter "Category=eval"</c>.
/// </summary>
[Trait("Category", "eval")]
[Trait("Category", "live")]
public class ExtractionFidelityEvalTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Extraction_fidelity_does_not_regress_below_the_committed_baseline()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live extraction eval needs a Gemini API key in GEMINI_API_KEY.");

        var extractor = new JdRequirementExtractor(LiveGemini.CreateChatClient());
        var aggregate = await ExtractionEvalRunner.RunAsync(
            extractor, GoldenJdSet.Load(), TimeSpan.FromSeconds(5), output.WriteLine);

        output.WriteLine("");
        output.WriteLine(
            $"recall={aggregate.ConceptRecall:F4} mustHaveP={aggregate.MustHavePrecision:F4} " +
            $"verbatim={aggregate.EvidenceVerbatimRate:F4} seniority={aggregate.SeniorityAccuracy:F4} " +
            $"location={aggregate.LocationAccuracy:F4} fabrications={aggregate.FabricationCount} " +
            $"faults={aggregate.FaultCount}");

        ExtractionEvalRunner.GateViolations(aggregate).Should().BeEmpty();
    }
}
