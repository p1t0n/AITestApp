using CvManager.Application.Abstractions;
using CvManager.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CvManager.Application.Tests;

/// <summary>
/// Live smoke test: embeds a string against the real configured endpoint (GitHub Models by
/// default). Excluded from the default run — it proves the PAT actually serves an embedding
/// deployment (the open risk in the RAG plan). Run on demand:
/// <c>dotnet test --filter "Category=live"</c> with a PAT in <c>GITHUB_TOKEN</c>.
/// </summary>
[Trait("Category", "live")]
public class EmbeddingLiveSmokeTests
{
    [SkippableFact]
    public async Task Embeds_a_string_to_a_1536_dim_vector()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
            "Live embedding smoke test needs a GitHub Models PAT in GITHUB_TOKEN.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubModels:Endpoint"] = "https://models.github.ai/inference",
                ["GitHubModels:EmbeddingModel"] = "text-embedding-3-small",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddGitHubModelsEmbeddings(config)
            .BuildServiceProvider();

        var embedder = provider.GetRequiredService<IEmbedder>();
        var batch = await embedder.EmbedAsync(["a senior backend engineer who led a payments rewrite"]);

        batch.Vectors.Should().ContainSingle();
        batch.Vectors[0].Should().HaveCount(1536);
        batch.InputTokens.Should().BeGreaterThan(0);
    }
}
