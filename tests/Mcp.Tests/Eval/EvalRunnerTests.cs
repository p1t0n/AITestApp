using EmployeeManager.Application.Abstractions;
using EmployeeManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Plumbing test for <see cref="EvalRunner"/> against real pgvector with a deterministic keyword
/// embedder: proves seeding, indexing via the real reconciler, query execution, id-to-key mapping,
/// and metric aggregation — everything except embedding quality, which only the live eval measures.
/// </summary>
public sealed class EvalRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Runs_the_golden_set_end_to_end_and_computes_hand_checkable_metrics()
    {
        var corpus = new[]
        {
            Person("fiona-fintech", "Fiona", "Built fintech trading systems."),
            Person("pat-payments", "Pat", "Ran a fintech payments platform."),
            Person("gary-gaming", "Gary", "Wrote gaming engines."),
        };
        var goldenSet = new[]
        {
            // Both mention "fintech": full recall, first hit at rank 1.
            new GoldenQuery("fintech", GoldenQueryCategory.Keyword, ["fiona-fintech", "pat-payments"]),
            // Only Gary mentions "gaming".
            new GoldenQuery("gaming", GoldenQueryCategory.Keyword, ["gary-gaming"]),
            // Fiona never mentions "payments": 1 of 2 expected found -> recall 0.5, RR 1 (Pat first).
            new GoldenQuery("payments", GoldenQueryCategory.Keyword, ["pat-payments", "fiona-fintech"]),
            // Nobody mentions "logistics": must return no one.
            new GoldenQuery("logistics", GoldenQueryCategory.Negative, []),
        };

        var result = await EvalRunner.RunAsync(NewDb, new KeywordEmbedder(), corpus, goldenSet);

        // recall@5 = mean(1, 1, 0.5) ; MRR = mean(1, 1, 1) ; negatives stayed empty.
        result.Metrics.RecallAt5.Should().BeApproximately(5.0 / 6.0, 1e-9);
        result.Metrics.MeanReciprocalRank.Should().Be(1.0);
        result.Metrics.NegativeFalsePositiveRate.Should().Be(0);

        result.Traces.Should().HaveCount(4);
        result.Traces[0].ReturnedKeys.Should().Equal("fiona-fintech", "pat-payments");
        result.Traces[3].ReturnedKeys.Should().BeEmpty();
    }

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static EvalEmployee Person(string key, string firstName, string narrative) => new(
        Key: key,
        FirstName: firstName,
        LastName: "Eval",
        Title: "Engineer",
        Location: "Remote",
        Summary: narrative,
        Experiences:
        [
            new EvalExperience("Acme", "Engineer", "2020-01", null, narrative, []),
        ]);

    /// <summary>Topical fake embedder (same trick as SemanticSearchServiceTests): keywords map to
    /// basis dimensions, plus a tiny baseline so no vector is all-zero.</summary>
    private sealed class KeywordEmbedder : IEmbedder
    {
        private static readonly string[] Vocab = ["fintech", "gaming", "payments", "logistics"];

        public string Model => "keyword-embedder";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => Task.FromResult(new EmbeddingBatch(inputs.Select(Vectorize).ToList(), inputs.Count));

        private static float[] Vectorize(string text)
        {
            var lower = text.ToLowerInvariant();
            var v = new float[1536];
            v[1000] = 0.01f; // baseline
            for (var i = 0; i < Vocab.Length; i++)
            {
                if (lower.Contains(Vocab[i]))
                {
                    v[i] = 1f;
                }
            }

            return v;
        }
    }
}
