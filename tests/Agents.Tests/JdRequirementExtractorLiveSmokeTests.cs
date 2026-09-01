using System.ClientModel;
using System.ClientModel.Primitives;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Live smoke for the JD requirement extractor (P1T-116): one real extraction on the pinned
/// model, asserting the reply is schema-valid and the honesty surface is populated sanely.
/// No MCP server or database needed. Run with <c>dotnet test --filter "Category=live"</c>.
/// </summary>
[Trait("Category", "live")]
public class JdRequirementExtractorLiveSmokeTests
{
    [SkippableFact]
    public async Task Extracts_a_schema_valid_reading_of_a_real_jd()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Skip.If(string.IsNullOrWhiteSpace(apiKey), "Live smoke needs GEMINI_API_KEY.");

        var cfg = new GeminiOptions();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint),
            Transport = new HttpClientPipelineTransport(
                new HttpClient(new GeminiCompatHandler(new HttpClientHandler()))),
        };
        options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);
        var chat = new OpenAIClient(new ApiKeyCredential(apiKey!), options)
            .GetChatClient(cfg.Model)
            .AsIChatClient();
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(
            "Senior backend engineer for a payments platform: 5+ years building event " +
            "streaming systems, cloud infrastructure (AWS preferred), and experience " +
            "leading small teams. Amsterdam or remote within CET.");

        outcome.FaultDetail.Should().BeNull();
        var result = outcome.Requirements!;
        result.Requirements.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(8);
        result.Requirements.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Text));
        // Verified (non-inferred) requirements must carry a quote that survived verification.
        result.Requirements.Where(r => !r.Inferred)
            .Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.EvidenceSpan));
        result.Seniority.Should().Be(JdSeniority.Senior);
        outcome.Reply.TotalTokens.Should().BeGreaterThan(0);
    }
}
