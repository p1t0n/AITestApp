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
/// the MCP host reads, so this gate measures whatever a deployment is actually configured to run.
/// The committed baseline is Gemini's; EXP-67 adds the per-provider floors.</para>
/// </summary>
[Trait("Category", "live")]
public class RetrievalEvalLiveTests
{
    private readonly ITestOutputHelper _output;

    public RetrievalEvalLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Recall_at_5_does_not_regress_below_the_committed_baseline()
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var (provider, embeddingOptions) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);

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
            .BeGreaterThanOrEqualTo(EvalBaselines.RecallAt5 - EvalBaselines.Tolerance,
                "retrieval quality must not regress below the committed baseline");
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
