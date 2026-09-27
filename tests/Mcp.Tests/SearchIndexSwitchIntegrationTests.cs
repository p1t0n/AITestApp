using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The whole of EXP-65 end to end, against real pgvector: a deployment running on one embeddings
/// provider is switched to another, and the index converges to full coverage without ever
/// comparing vectors across the two.
///
/// <para>The two fake embedders are <b>deliberately incompatible</b>: each puts its topic on a
/// different basis dimension, so a Gemini vector and an Azure vector for the same text are
/// orthogonal. That is what makes a leak visible — without the tag filter, mid-switch search would
/// not error, it would quietly rank the wrong people, which is the failure this ticket exists to
/// make impossible.</para>
/// </summary>
public sealed class SearchIndexSwitchIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Gemini_to_Azure_switch_converges_to_full_coverage()
    {
        await using var seeding = NewDb();
        await seeding.Database.MigrateAsync();
        SeedRoster(seeding);

        // ---- before: fully indexed on Gemini, search works ----
        var warm = await Reconciler(NewDb(), Gemini).RunOnceAsync();
        warm.Embedded.Should().Be(ChunkCount);
        warm.Coverage.Should().Be(1.0);

        var beforeSwitch = await Search(Gemini).SearchAsync("fintech");
        beforeSwitch.Results.Select(r => r.Name).Should().Contain("Fiona Fintech");
        beforeSwitch.CoverageNote.Should().BeNull();

        // ---- the switch: Ai:Embeddings:Provider changed, nothing has re-embedded yet ----
        var midSwitch = Search(Azure);
        var atZero = await midSwitch.SearchAsync("fintech");
        atZero.Error.Should().BeNull("an incomplete index is not a broken one");
        atZero.Results.Should().BeEmpty("no vector in the index is Azure's yet");
        atZero.CoverageNote.Should().StartWith("index rebuilding: 0% re-embedded");

        // ---- an interrupted rebuild: the provider dies after 2 of the 6 chunks ----
        var interrupted = new FailAfterEmbedder(Azure, allowedBatches: 2);
        await Reconciler(NewDb(), interrupted, batchSize: 1)
            .Invoking(r => r.RunOnceAsync()).Should().ThrowAsync<InvalidOperationException>();

        await using (var mid = NewDb())
        {
            var coverage = await IndexCoverage.MeasureAsync(mid, Azure.Tag, CancellationToken.None);
            coverage.Should().BeApproximately(2d / ChunkCount, 1e-9);

            // The four still on Gemini are neither compared against nor blanked: they are there,
            // under their own tag, ready to serve again if the switch is reverted.
            (await mid.ExpertSearchChunks.CountAsync(c => c.Embedding == null)).Should().Be(0);
            (await mid.ExpertSearchChunks.CountAsync(c => c.Model == Gemini.Tag)).Should().Be(ChunkCount - 2);
        }

        var partial = await Search(Azure).SearchAsync("fintech");
        partial.CoverageNote.Should().StartWith("index rebuilding: 33% re-embedded");

        // ---- convergence ----
        var finishing = await Reconciler(NewDb(), Azure).RunOnceAsync();
        finishing.Embedded.Should().Be(ChunkCount - 2);
        finishing.Coverage.Should().Be(1.0);

        var afterSwitch = await Search(Azure).SearchAsync("fintech");
        afterSwitch.Results.Select(r => r.Name).Should().Contain("Fiona Fintech");
        afterSwitch.CoverageNote.Should().BeNull();

        // And the index is wholly Azure's — no row left behind under the old tag.
        await using var settled = NewDb();
        (await settled.ExpertSearchChunks.AsNoTracking().ToListAsync())
            .Should().OnlyContain(c => c.Model == Azure.Tag);
        (await Reconciler(NewDb(), Azure).RunOnceAsync()).DidWork.Should().BeFalse();
    }

    /// <summary>Two experts × (1 summary + 1 experience + 1 achievement bullet).</summary>
    private const int ChunkCount = 6;

    private static readonly TopicEmbedder Gemini = new("Gemini", "gemini-embedding-001", basis: 0);
    private static readonly TopicEmbedder Azure = new("AzureFoundry", "text-embedding-3-small", basis: 500);

    private ISemanticSearchService Search(IEmbedder embedder) => new SemanticSearchService(
        NewDb(), embedder,
        Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

    private static SearchIndexReconciler Reconciler(
        AppDbContext db, IEmbedder embedder, int batchSize = 32) => new(
        db, embedder,
        Options.Create(new SearchIndexOptions { EmbedBatchSize = batchSize }),
        new SearchIndexMetrics(),
        NullLogger<SearchIndexReconciler>.Instance);

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    private static void SeedRoster(AppDbContext db)
    {
        db.Experts.Add(Expert("Fiona", "Fintech", "Led a fintech payments platform migration."));
        db.Experts.Add(Expert("Gary", "Gaming", "Built a gaming physics engine."));
        db.SaveChanges();
    }

    private static Expert Expert(string first, string last, string narrative) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = first,
        LastName = last,
        Title = "Engineer",
        Email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
        Summary = narrative,
        Experiences =
        [
            new Experience
            {
                Id = Guid.NewGuid(),
                Company = "Acme",
                Title = "Engineer",
                StartDate = new DateOnly(2020, 1, 1),
                Summary = narrative,
                Achievements = [new Achievement { Order = 1, Text = narrative }],
            },
        ],
    };

    /// <summary>
    /// A topical embedder whose vocabulary lives at <paramref name="basis"/>. Two instances with
    /// different bases produce orthogonal vectors for the same text: a cross-model comparison
    /// cannot clear the similarity floor, so a leak shows up as a missing result rather than as a
    /// coincidence that happens to still look right.
    /// </summary>
    private sealed class TopicEmbedder(string provider, string model, int basis) : IEmbedder
    {
        private static readonly string[] Vocab = ["fintech", "gaming", "payments"];

        public string Model => model;

        public string Tag => $"{provider}/{model}";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => Task.FromResult(new EmbeddingBatch(inputs.Select(Vectorize).ToList(), inputs.Count));

        private float[] Vectorize(string text)
        {
            var lower = text.ToLowerInvariant();
            var v = new float[1536];
            v[1400 + basis / 500] = 0.01f; // per-provider baseline; pgvector rejects an all-zero vector
            for (var i = 0; i < Vocab.Length; i++)
            {
                if (lower.Contains(Vocab[i]))
                {
                    v[basis + i] = 1f;
                }
            }

            return v;
        }
    }

    /// <summary>A provider that goes away partway through the rebuild.</summary>
    private sealed class FailAfterEmbedder(IEmbedder inner, int allowedBatches) : IEmbedder
    {
        private int _batches;

        public string Model => inner.Model;

        public string Tag => inner.Tag;

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => _batches++ < allowedBatches
                ? inner.EmbedAsync(inputs, ct)
                : throw new InvalidOperationException("the provider went away mid-rebuild");
    }
}
