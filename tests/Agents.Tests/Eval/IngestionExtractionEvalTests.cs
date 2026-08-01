using CvManager.Agents.Agents;
using CvManager.Agents.Configuration;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace CvManager.Agents.Tests.Eval;

/// <summary>
/// Live ingestion-extraction eval (P1T-97), graduated from the P1T-81 gate prototype. Runs the
/// REAL <see cref="ResumeIngestionAgent"/> — production instructions, production self-correction
/// behavior — against the real model, with fake MCP tools that validate through the real
/// Application validators and record every staged write. Excluded from the default run:
/// <c>GEMINI_API_KEY=&lt;key&gt; dotnet test tests/Agents.Tests --filter "Category=eval"</c>.
/// Floors live in <see cref="AgentEvalBaselines"/>.
/// </summary>
[Trait("Category", "eval")]
public class IngestionExtractionEvalTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Ingestion_extraction_does_not_regress_below_the_committed_baseline()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live ingestion eval needs a Gemini API key in GEMINI_API_KEY.");

        var chatClient = LiveGemini.CreateChatClient();
        var scores = new List<IngestionFixtureScore>();

        foreach (var fixture in Fixtures.All)
        {
            var (tools, log, catalog) = IngestionEvalTools.Create();
            var agent = new ResumeIngestionAgent(
                chatClient, new FakeToolSource(tools.ToArray()), NullLoggerFactory.Instance);

            var outcome = await agent.IngestAsync(fixture.Text);
            var proposals = ParseProposals(outcome.ClosingJson);
            var score = IngestionEvalScorer.Score(fixture, log, catalog, proposals);
            scores.Add(score);
            output.WriteLine(
                $"{score.FixtureId,-20} fields {score.FieldsCorrect}/4  skills R{score.SkillRecall:P0}/P{score.SkillPrecision:P0}  " +
                $"halluc {score.HallucinatedSkills}  exp {score.ExperiencesMatched}/{score.ExperiencesExpected}  " +
                $"dateErr {score.DateErrors}  rejects {log.ValidationRejections}  proposals [{string.Join(", ", score.Proposals)}]" +
                (score.Notes.Length > 0 ? $"  | {score.Notes}" : ""));

            await Task.Delay(TimeSpan.FromSeconds(10)); // free-tier RPM headroom
        }

        var fieldAccuracy = scores.Sum(s => s.FieldsCorrect) / (scores.Count * 4.0);
        var skillRecall = scores.Average(s => s.SkillRecall);
        var skillPrecision = scores.Average(s => s.SkillPrecision);
        var experienceMatch = (double)scores.Sum(s => s.ExperiencesMatched) / scores.Sum(s => s.ExperiencesExpected);

        output.WriteLine("");
        output.WriteLine($"field accuracy   = {fieldAccuracy:F4}");
        output.WriteLine($"skill recall     = {skillRecall:F4}");
        output.WriteLine($"skill precision  = {skillPrecision:F4}");
        output.WriteLine($"experience match = {experienceMatch:F4}");
        output.WriteLine($"hallucinated     = {scores.Sum(s => s.HallucinatedSkills)}");
        output.WriteLine($"fabricated email = {scores.Count(s => s.FabricatedEmail)}");
        output.WriteLine($"date errors      = {scores.Sum(s => s.DateErrors)}");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            fieldAccuracy.Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.IngestionFieldAccuracyFloor);
            skillRecall.Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.IngestionSkillRecallFloor);
            skillPrecision.Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.IngestionSkillPrecisionFloor);
            experienceMatch.Should().BeGreaterThanOrEqualTo(AgentEvalBaselines.IngestionExperienceMatchFloor);
            scores.Sum(s => s.HallucinatedSkills).Should()
                .BeLessThanOrEqualTo(AgentEvalBaselines.IngestionHallucinatedSkillsCeiling);
            scores.Count(s => s.FabricatedEmail).Should()
                .BeLessThanOrEqualTo(AgentEvalBaselines.IngestionFabricatedEmailsCeiling);
            scores.Sum(s => s.DateErrors).Should()
                .BeLessThanOrEqualTo(AgentEvalBaselines.IngestionDateErrorsCeiling);
        }
    }

    private static IReadOnlyList<string> ParseProposals(string closingJson)
    {
        var start = closingJson.IndexOf('{');
        var end = closingJson.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return [];
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(closingJson[start..(end + 1)]);
            if (doc.RootElement.TryGetProperty("proposals", out var proposals)
                && proposals.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return proposals.EnumerateArray()
                    .Where(p => p.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(p => p.GetString()!)
                    .ToList();
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return [];
    }
}

/// <summary>Builds the same instrumented Gemini chat client production uses (thought-signature
/// policy + compat handler), keyed off <c>GEMINI_API_KEY</c>.</summary>
internal static class LiveGemini
{
    public static Microsoft.Extensions.AI.IChatClient CreateChatClient()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new ServiceCollection()
            .AddLogging()
            .AddGeminiChatClient(config)
            .BuildServiceProvider();
        // Eval-only patience: a burst of tool-loop turns trips the free tier's per-minute cap
        // mid-run; production fast-fails 429s by design, the eval waits the window out instead.
        return new Retry429ChatClient(provider.GetRequiredService<Microsoft.Extensions.AI.IChatClient>());
    }

    private sealed class Retry429ChatClient(Microsoft.Extensions.AI.IChatClient inner)
        : Microsoft.Extensions.AI.DelegatingChatClient(inner)
    {
        public override async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await base.GetResponseAsync(messages, options, cancellationToken);
                }
                catch (System.ClientModel.ClientResultException ex) when (ex.Status == 429 && attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20 * attempt), cancellationToken);
                }
            }
        }
    }
}
