using CvManager.Application.Abstractions;
using CvManager.Infrastructure.Embeddings;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

using CvManager.RetrievalEval;

namespace CvManager.Mcp.Tests.Eval;

/// <summary>
/// The retrieval-quality regression gate: seeds the frozen corpus into real pgvector, embeds it via
/// the REAL GitHub Models pipeline (real embeddings are the point — fakes measure plumbing, not
/// meaning), runs the golden set, and asserts recall@5 has not regressed below the committed
/// baseline. Excluded from the default run; needs Docker and a PAT:
/// <c>dotnet test --filter "Category=live"</c> with <c>GITHUB_TOKEN</c> set.
/// </summary>
[Trait("Category", "live")]
public class RetrievalEvalLiveTests
{
    private readonly ITestOutputHelper _output;

    public RetrievalEvalLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Recall_at_5_does_not_regress_below_the_committed_baseline()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
            "Live retrieval eval needs a GitHub Models PAT in GITHUB_TOKEN.");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .Build();
        await postgres.StartAsync();

        AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);

        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync();
        }

        using var provider = BuildRealEmbeddingProvider();
        var embedder = provider.GetRequiredService<IEmbedder>();

        var result = await EvalRunner.RunAsync(
            NewDb, embedder, EvalFixtures.LoadCorpus(), EvalFixtures.LoadGoldenSet());

        Report(result);

        result.Metrics.RecallAt5.Should()
            .BeGreaterThanOrEqualTo(EvalBaselines.RecallAt5 - EvalBaselines.Tolerance,
                "retrieval quality must not regress below the committed baseline");
    }

    /// <summary>The same real embedding registration production uses (AddGitHubModelsEmbeddings).</summary>
    private static ServiceProvider BuildRealEmbeddingProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubModels:Endpoint"] = "https://models.github.ai/inference",
                ["GitHubModels:EmbeddingModel"] = "text-embedding-3-small",
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddGitHubModelsEmbeddings(config)
            .BuildServiceProvider();
    }

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
