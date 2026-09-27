using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

using ExpertToJob.RetrievalEval;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// The retrieval-quality regression gate: seeds the frozen corpus into real pgvector, embeds it
/// through the REAL configured provider (real embeddings are the point — fakes measure plumbing,
/// not meaning), runs the golden set, and asserts recall@5 has not regressed below the committed
/// baseline. Excluded from the default run; needs Docker and the active provider's key:
/// <c>dotnet test --filter "Category=live"</c>.
///
/// <para>Since EXP-64 the provider, its model and its similarity floor all come from the same seam
/// the MCP host reads. Since EXP-67 there is <b>a gate per provider</b>, each pinned to its own
/// measured floor and each skipping on its own missing key (ADR §2 decision 8) — so exporting one
/// vendor's key runs one gate and says nothing about the other, rather than silently measuring
/// whichever provider happened to be configured.</para>
///
/// <para>The provider is forced into the configuration this test builds rather than read out of
/// it: a gate that only ever measured the shipped default could not have caught the number that
/// moved when the default did.</para>
/// </summary>
[Trait("Category", "live")]
public class RetrievalEvalLiveTests
{
    private readonly ITestOutputHelper _output;

    public RetrievalEvalLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public Task Azure_floor_holds() => FloorHoldsFor(EmbeddingsProvider.AzureFoundry);

    [SkippableFact]
    public Task Gemini_floor_holds() => FloorHoldsFor(EmbeddingsProvider.Gemini);

    private async Task FloorHoldsFor(EmbeddingsProvider provider)
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EmbeddingServiceCollectionExtensions.ProviderKey] = provider.ToString(),
            })
            .Build();

        var embeddingOptions = EmbeddingServiceCollectionExtensions.ResolveProvider(config).Options;

        Skip.If(
            string.IsNullOrWhiteSpace(EmbeddingServiceCollectionExtensions.ResolveApiKey(
                provider, embeddingOptions, Environment.GetEnvironmentVariable)),
            $"Live retrieval eval needs a {provider} API key in "
            + $"{EmbeddingOptions.ApiKeyVariableFor(provider)}.");

        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .Build();
        await postgres.StartAsync();

        AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);

        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync();
        }

        using var services = BuildRealEmbeddingProvider(config);
        var embedder = services.GetRequiredService<IEmbedder>();

        var result = await EvalRunner.RunAsync(
            NewDb, embedder, EvalFixtures.LoadCorpus(), EvalFixtures.LoadGoldenSet(),
            embeddingOptions.MinSimilarity);

        Report(result);

        result.Metrics.RecallAt5.Should()
            .BeGreaterThanOrEqualTo(EvalBaselines.RecallAt5For(provider) - EvalBaselines.Tolerance,
                $"retrieval quality on {provider} must not regress below its committed baseline, "
                + $"measured at that provider's own threshold {embeddingOptions.MinSimilarity}");
    }

    /// <summary>The same real embedding registration production uses (AddEmbeddingProvider).</summary>
    private static ServiceProvider BuildRealEmbeddingProvider(IConfiguration config)
        => new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

    /// <summary>Full metric readout plus the misbehaving queries — the eval's actual product.</summary>
    private void Report(EvalRunResult result)
    {
        _output.WriteLine($"recall@5 = {result.Metrics.RecallAt5:F4}");
        _output.WriteLine($"MRR      = {result.Metrics.MeanReciprocalRank:F4}");
        _output.WriteLine($"neg FP   = {result.Metrics.NegativeFalsePositiveRate:F4}");

        foreach (var trace in result.Traces)
        {
            var missed = trace.Query.Expected.Except(trace.ReturnedKeys.Take(5)).ToList();
            var isFalsePositive = trace.Query.Category == GoldenQueryCategory.Negative
                                  && trace.ReturnedKeys.Count > 0;
            if (missed.Count > 0 || isFalsePositive)
            {
                _output.WriteLine(
                    $"[{trace.Query.Category}] '{trace.Query.Query}' -> " +
                    $"returned [{string.Join(", ", trace.ReturnedKeys)}], " +
                    $"missed [{string.Join(", ", missed)}]");
            }
        }
    }
}
