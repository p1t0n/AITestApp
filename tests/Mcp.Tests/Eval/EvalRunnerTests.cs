using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Search;
using ExpertToJob.RetrievalEval;
using ExpertToJob.Application.Search;
using ExpertToJob.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Polly;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Plumbing test for <see cref="EvalRunner"/> against real pgvector with a deterministic keyword
/// embedder: proves seeding, indexing via the real reconciler, query execution, id-to-key mapping,
/// and metric aggregation — everything except embedding quality, which only the live eval measures.
/// </summary>
public sealed class EvalRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
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

        var result = await EvalRunner.RunAsync(
            NewDb, new KeywordEmbedder(), corpus, goldenSet,
            // The plumbing measurement is against the incumbent's floor, which is what a bare
            // SemanticSearchOptions also carries; stated rather than defaulted since EXP-64.
            new SemanticSearchOptions().MinSimilarity);

        // recall@5 = mean(1, 1, 0.5) ; MRR = mean(1, 1, 1) ; negatives stayed empty.
        result.Metrics.RecallAt5.Should().BeApproximately(5.0 / 6.0, 1e-9);
        result.Metrics.MeanReciprocalRank.Should().Be(1.0);
        result.Metrics.NegativeFalsePositiveRate.Should().Be(0);

        result.Traces.Should().HaveCount(4);
        result.Traces[0].ReturnedKeys.Should().Equal("fiona-fintech", "pat-payments");
        result.Traces[3].ReturnedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_retries_transient_embedding_failures_between_queries()
    {
        var corpus = new[] { Person("fiona-fintech", "Fiona", "Built fintech trading systems.") };
        var goldenSet = new[]
        {
            new GoldenQuery("fintech", GoldenQueryCategory.Keyword, ["fiona-fintech"]),
            new GoldenQuery("gaming", GoldenQueryCategory.Negative, []),
        };

        // Fails every second call: indexing (call 1) succeeds, each query's first attempt dies the
        // way a rate-limited provider does, and the retry succeeds.
        var flaky = new FlakyEmbedder(new KeywordEmbedder());

        var cached = await EvalRunner.CaptureAsync(
            NewDb, flaky, corpus, goldenSet, floorSimilarity: 0.15,
            retry: QueryRetry.Ladder(maxAttempts: 2, TimeSpan.Zero, TimeProvider.System));

        cached.Should().HaveCount(2);
        cached[0].Hits.Select(h => h.Key).Should().Equal("fiona-fintech");
        cached[1].Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_fails_after_the_retry_budget_is_spent()
    {
        var corpus = new[] { Person("fiona-fintech", "Fiona", "Built fintech trading systems.") };
        var goldenSet = new[] { new GoldenQuery("fintech", GoldenQueryCategory.Keyword, ["fiona-fintech"]) };

        var act = () => EvalRunner.CaptureAsync(
            NewDb, new FlakyEmbedder(new KeywordEmbedder()), corpus, goldenSet, floorSimilarity: 0.15,
            retry: QueryRetry.Ladder(maxAttempts: 1, TimeSpan.Zero, TimeProvider.System));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*fintech*");
    }

    /// <summary>The shipped soft-error ladder, built on <paramref name="clock"/>. The one line
    /// that knows how the ladder is constructed — the freeze test reads only its behaviour.</summary>
    private static ResiliencePipeline<SemanticSearchResult> QueryLadder(TimeProvider clock) =>
        QueryRetry.Default(clock);

    /// <summary>
    /// The shipped ladder itself, frozen (EXP-87): five attempts, waiting 20s, 40s, 60s then 80s.
    /// Measured on a fake clock, so the assertion is about the schedule rather than elapsed
    /// wall-clock time — the real ladder would take over three minutes to spend.
    /// </summary>
    [Fact]
    public async Task The_shipped_query_ladder_climbs_in_twenty_second_steps_over_five_attempts()
    {
        var corpus = new[] { Person("fiona-fintech", "Fiona", "Built fintech trading systems.") };
        var goldenSet = new[] { new GoldenQuery("fintech", GoldenQueryCategory.Keyword, ["fiona-fintech"]) };
        var clock = new LadderClock();

        var act = () => EvalRunner.CaptureAsync(
            NewDb, new QueryFailingEmbedder(new KeywordEmbedder(), "fintech"), corpus, goldenSet,
            floorSimilarity: 0.15,
            retry: QueryLadder(clock));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*5 attempt(s)*");
        clock.Waits.Should().Equal(
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(80));
    }

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static EvalExpert Person(string key, string firstName, string narrative) => new(
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

    /// <summary>Throws on every second call — the shape of a rate-limited provider: the indexing
    /// call goes through, then each query's first attempt dies and its retry succeeds.</summary>
    private sealed class FlakyEmbedder(IEmbedder inner) : IEmbedder
    {
        private int _calls;

        public string Model => inner.Model;

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => ++_calls % 2 == 0
                ? throw new HttpRequestException("429 simulated rate limit")
                : inner.EmbedAsync(inputs, ct);
    }

    /// <summary>Indexes the corpus happily and rate-limits every query — the shape a provider shows
    /// when the per-minute budget is spent mid-run, so the ladder is the only thing left to measure.</summary>
    private sealed class QueryFailingEmbedder(IEmbedder inner, string query) : IEmbedder
    {
        public string Model => inner.Model;

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => inputs is [var only] && only == query
                ? throw new HttpRequestException("429 simulated rate limit")
                : inner.EmbedAsync(inputs, ct);
    }

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
