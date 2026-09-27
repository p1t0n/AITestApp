using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Live smoke test: embeds a string against a real endpoint, through the same seam the MCP host
/// registers (EXP-64). Excluded from the default run — it proves the key actually serves an
/// embedding model at the pinned 1536 dimensionality. Run on demand:
/// <c>dotnet test --filter "Category=live"</c> with a key in either provider's variable.
///
/// <para><b>One case per provider since EXP-67</b> (ADR §2 decision 8), each skipping on its own
/// missing key. A single test that read the configured provider would prove only whichever one the
/// shipped settings happened to name, which is the half nobody needed convincing about.</para>
/// </summary>
[Trait("Category", "live")]
public class EmbeddingLiveSmokeTests
{
    [SkippableTheory]
    [InlineData(EmbeddingsProvider.AzureFoundry)]
    [InlineData(EmbeddingsProvider.Gemini)]
    public async Task Embeds_a_string_to_a_1536_dim_vector(EmbeddingsProvider provider)
    {
        // Endpoint and model come from the provider's own defaults, which the seam supplies; only
        // the discriminator is forced, so this measures the settings a deployment would run on.
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EmbeddingServiceCollectionExtensions.ProviderKey] = provider.ToString(),
            })
            .Build();

        var options = EmbeddingServiceCollectionExtensions.ResolveProvider(config).Options;
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
        // The tag, not just the model: it is what every vector is stamped with and what all three
        // search paths compare within (EXP-65), so a live run is the one place it can be shown to
        // be the tag a real provider's real embedder produces.
        embedder.Tag.Should().Be($"{provider}/{options.EmbeddingModel}");
        // Gemini's OpenAI-compat embeddings endpoint reports no usage block, so token count is
        // best-effort zero there (embedding spend is logged for visibility, never charged to caps).
        batch.InputTokens.Should().BeGreaterThanOrEqualTo(0);
    }
}
