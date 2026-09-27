using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Live smoke test: embeds a string against the real configured endpoint, through the same seam
/// the MCP host registers (EXP-64). Excluded from the default run — it proves the key actually
/// serves an embedding model at the pinned 1536 dimensionality. Run on demand:
/// <c>dotnet test --filter "Category=live"</c> with a key in the active provider's variable.
/// </summary>
[Trait("Category", "live")]
public class EmbeddingLiveSmokeTests
{
    [SkippableFact]
    public async Task Embeds_a_string_to_a_1536_dim_vector()
    {
        // Provider, endpoint and model all come from configuration — the endpoint literals this
        // test used to carry were the Gemini branch's own defaults, which the seam now supplies.
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
        var keyVariable = EmbeddingOptions.ApiKeyVariableFor(provider);

        Skip.If(
            string.IsNullOrWhiteSpace(EmbeddingServiceCollectionExtensions.ResolveApiKey(
                provider, options, Environment.GetEnvironmentVariable)),
            $"Live embedding smoke test needs a {provider} API key in {keyVariable}.");

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        var embedder = services.GetRequiredService<IEmbedder>();
        var batch = await embedder.EmbedAsync(["a senior backend engineer who led a payments rewrite"]);

        batch.Vectors.Should().ContainSingle();
        batch.Vectors[0].Should().HaveCount(options.Dimensions);
        // Gemini's OpenAI-compat embeddings endpoint reports no usage block, so token count is
        // best-effort zero there (embedding spend is logged for visibility, never charged to caps).
        batch.InputTokens.Should().BeGreaterThanOrEqualTo(0);
    }
}
