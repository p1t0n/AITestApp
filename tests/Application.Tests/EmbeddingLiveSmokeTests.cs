using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Live smoke test: embeds a string against the real configured endpoint (Gemini by
/// default). Excluded from the default run — it proves the key actually serves an embedding
/// model at the pinned 1536 dimensionality. Run on demand:
/// <c>dotnet test --filter "Category=live"</c> with a key in <c>GEMINI_API_KEY</c>.
/// </summary>
[Trait("Category", "live")]
public class EmbeddingLiveSmokeTests
{
    [SkippableFact]
    public async Task Embeds_a_string_to_a_1536_dim_vector()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            "Live embedding smoke test needs a Gemini API key in GEMINI_API_KEY.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai",
                ["Gemini:EmbeddingModel"] = "gemini-embedding-001",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddGeminiEmbeddings(config)
            .BuildServiceProvider();

        var embedder = provider.GetRequiredService<IEmbedder>();
        var batch = await embedder.EmbedAsync(["a senior backend engineer who led a payments rewrite"]);

        batch.Vectors.Should().ContainSingle();
        batch.Vectors[0].Should().HaveCount(1536);
        // Gemini's OpenAI-compat embeddings endpoint reports no usage block, so token count is
        // best-effort zero there (embedding spend is logged for visibility, never charged to caps).
        batch.InputTokens.Should().BeGreaterThanOrEqualTo(0);
    }
}
