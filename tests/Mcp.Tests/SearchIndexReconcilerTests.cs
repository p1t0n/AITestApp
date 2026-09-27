using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Integration tests for <see cref="SearchIndexReconciler"/> against a real pgvector Postgres
/// (Testcontainers). Uses a deterministic fake embedder — the pgvector column, migrations, and the
/// reconcile/backfill loop are what's under test, not the embedding provider.
/// </summary>
public sealed class SearchIndexReconcilerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task First_pass_backfills_all_chunks_with_embeddings()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Senior backend engineer.", experiences: 2);

        var report = await Reconciler(db).RunOnceAsync();

        // 1 summary + 2 experience + 2 achievement-bullet chunks, all embedded on the first
        // (backfill) pass.
        report.Inserted.Should().Be(5);
        report.Embedded.Should().Be(5);
        report.EmbeddingTokens.Should().BeGreaterThan(0);

        var chunks = await db.ExpertSearchChunks.Where(c => c.ExpertId == expert.Id).ToListAsync();
        chunks.Should().HaveCount(5);
        chunks.Count(c => c.SourceType == SearchChunkSource.Achievement).Should().Be(2);
        chunks.Should().OnlyContain(c => c.Embedding != null && c.EmbeddedAt != null);
        chunks.Should().OnlyContain(c => c.Model == "fake-embedder");
    }

    [Fact]
    public async Task Second_pass_with_no_changes_does_no_work()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        SeedExpert(db, summary: "Bio.", experiences: 1);

        await Reconciler(db).RunOnceAsync();
        var second = await Reconciler(db).RunOnceAsync();

        second.DidWork.Should().BeFalse();
    }

    [Fact]
    public async Task Editing_one_experience_re_embeds_only_that_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var edited = expert.Experiences.First();
        edited.Summary = "Rewrote the whole thing.";
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        report.Inserted.Should().Be(0);
        report.Deleted.Should().Be(0);
        report.Updated.Should().Be(1);
        report.Embedded.Should().Be(1);
    }

    [Fact]
    public async Task Removing_an_experience_deletes_its_orphaned_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var toRemove = expert.Experiences.First();
        db.Experiences.Remove(toRemove);
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        // The experience chunk and its cascaded achievement's bullet chunk both go.
        report.Deleted.Should().Be(2);
        (await db.ExpertSearchChunks.CountAsync(c => c.SourceId == toRemove.Id)).Should().Be(0);
        var orphanedAchievementIds = toRemove.Achievements.Select(a => a.Id).ToList();
        (await db.ExpertSearchChunks.CountAsync(c => orphanedAchievementIds.Contains(c.SourceId))).Should().Be(0);
    }

    [Fact]
    public async Task Editing_a_bullet_re_embeds_its_chunk_and_the_parent_experience_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var parent = expert.Experiences.First();
        var edited = parent.Achievements.Single();
        edited.Text = "Shipped something entirely different.";
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        // The bullet's own chunk changes, and so does the parent experience chunk that rolls the
        // bullet into its narrative. Nothing else moves.
        report.Inserted.Should().Be(0);
        report.Deleted.Should().Be(0);
        report.Updated.Should().Be(2);
        report.Embedded.Should().Be(2);

        var bulletChunk = await db.ExpertSearchChunks.SingleAsync(c => c.SourceId == edited.Id);
        bulletChunk.Content.Should().Be("Shipped something entirely different.");
        bulletChunk.Embedding.Should().NotBeNull();
    }

    [Fact]
    public async Task Deleting_an_achievement_removes_its_chunk_and_updates_the_experience_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Bio.", experiences: 1);
        await Reconciler(db).RunOnceAsync();

        var doomed = expert.Experiences.Single().Achievements.Single();
        db.Achievements.Remove(doomed);
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        // The bullet chunk is orphaned; the parent experience chunk re-renders without the bullet.
        report.Deleted.Should().Be(1);
        report.Updated.Should().Be(1);
        (await db.ExpertSearchChunks.CountAsync(c => c.SourceId == doomed.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_an_expert_cascades_away_its_chunks()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var expert = SeedExpert(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        db.Experts.Remove(await db.Experts.FirstAsync(e => e.Id == expert.Id));
        await db.SaveChangesAsync();

        (await db.ExpertSearchChunks.CountAsync(c => c.ExpertId == expert.Id)).Should().Be(0);
    }

    // ---- provider/model tags and switch safety (EXP-65) ----

    /// <summary>A tagged embedder, as the seam builds one: the bare model plus who served it.</summary>
    private sealed class TaggedEmbedder(string provider, string model) : IEmbedder
    {
        public string Model => model;

        public string Tag => $"{provider}/{model}";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => Task.FromResult(new EmbeddingBatch(
                inputs.Select(i => Constant(i, provider)).ToList(), inputs.Count * 5L));

        /// <summary>Each provider gets its own vector for the same text, so a stale vector is
        /// distinguishable from a re-embedded one by value and not only by its tag.</summary>
        private static float[] Constant(string text, string provider)
        {
            var seed = provider.Length + (text.Length % 7);
            var vector = new float[1536];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (seed + i % 13) / 100f;
            }

            return vector;
        }
    }

    private static readonly TaggedEmbedder Gemini = new("Gemini", "gemini-embedding-001");
    private static readonly TaggedEmbedder Azure = new("AzureFoundry", "text-embedding-3-small");

    [Fact]
    public async Task Stamps_each_vector_with_provider_and_model()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        SeedExpert(db, summary: "Senior backend engineer.", experiences: 2);

        var report = await Reconciler(db, Gemini).RunOnceAsync();

        report.Embedded.Should().Be(5);
        report.Coverage.Should().Be(1.0);
        var chunks = await db.ExpertSearchChunks.AsNoTracking().ToListAsync();
        chunks.Should().OnlyContain(c => c.Model == "Gemini/gemini-embedding-001");
    }

    [Fact]
    public async Task Re_embeds_chunks_whose_tag_differs_from_the_active_embedder()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        SeedExpert(db, summary: "Senior backend engineer.", experiences: 2);
        await Reconciler(db, Gemini).RunOnceAsync();

        // Nothing about the roster changed — only the configured provider did.
        var afterSwitch = await Reconciler(db, Azure).RunOnceAsync();

        afterSwitch.Inserted.Should().Be(0);
        afterSwitch.Updated.Should().Be(0);
        afterSwitch.Deleted.Should().Be(0);
        afterSwitch.Relabelled.Should().Be(0, "a Gemini tag is not Azure's bare model name");
        afterSwitch.Embedded.Should().Be(5);
        afterSwitch.Coverage.Should().Be(1.0);

        var chunks = await db.ExpertSearchChunks.AsNoTracking().ToListAsync();
        chunks.Should().OnlyContain(c => c.Model == "AzureFoundry/text-embedding-3-small");

        // And the pass after it is idle again: "wrong tag" is staleness, not a standing condition.
        (await Reconciler(db, Azure).RunOnceAsync()).DidWork.Should().BeFalse();
    }

    [Fact]
    public async Task Overwrites_a_stale_vector_in_place_without_blanking_the_rest()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        SeedExpert(db, summary: "Senior backend engineer.", experiences: 2);
        await Reconciler(db, Gemini).RunOnceAsync();

        var before = await db.ExpertSearchChunks.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Embedding!.ToArray());

        // One chunk at a time, so the index is observed mid-switch rather than only at the ends.
        await Reconciler(db, Azure, batchSize: 1).RunOnceAsync();

        var after = await db.ExpertSearchChunks.AsNoTracking().ToListAsync();
        after.Should().HaveCount(before.Count, "re-embedding replaces rows, it does not delete them");
        after.Should().OnlyContain(c => c.Embedding != null,
            "a bulk blank would empty search for the whole rebuild");
        after.Should().OnlyContain(c => c.EmbeddedAt != null);
        after.Should().OnlyContain(c => !c.Embedding!.ToArray().SequenceEqual(before[c.Id]),
            "every vector is genuinely the new provider's, not the old one restamped");
    }

    [Fact]
    public async Task Legacy_bare_tag_matching_the_active_model_is_relabelled_others_count_as_stale()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        SeedExpert(db, summary: "Senior backend engineer.", experiences: 2);
        await Reconciler(db, Gemini).RunOnceAsync();

        // The index as P1T-88 left it: some rows stamped with the bare current model, some with
        // the bare name of the model before it. No row has ever carried a provider.
        var ids = await db.ExpertSearchChunks.AsNoTracking().OrderBy(c => c.Id)
            .Select(c => c.Id).ToListAsync();
        var current = ids.Take(3).ToList();
        var previous = ids.Skip(3).ToList();
        await db.ExpertSearchChunks.Where(c => current.Contains(c.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Model, "gemini-embedding-001"));
        await db.ExpertSearchChunks.Where(c => previous.Contains(c.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Model, "text-embedding-3-small"));
        var untouched = await db.ExpertSearchChunks.AsNoTracking()
            .Where(c => current.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Embedding!.ToArray());

        // A fresh context: the deployment that finds these rows is a process that has just
        // started, not one holding entities it loaded before the relabel.
        await using var restarted = NewDb();
        var report = await Reconciler(restarted, Gemini).RunOnceAsync();

        // The three already made by gemini-embedding-001 are relabelled, not re-embedded — that is
        // what spares the free tier a three-day rebuild it would gain nothing from.
        report.Relabelled.Should().Be(3);
        report.Embedded.Should().Be(2);
        report.Coverage.Should().Be(1.0);

        var after = await restarted.ExpertSearchChunks.AsNoTracking().ToListAsync();
        after.Should().OnlyContain(c => c.Model == "Gemini/gemini-embedding-001");
        after.Where(c => untouched.ContainsKey(c.Id))
            .Should().OnlyContain(c => c.Embedding!.ToArray().SequenceEqual(untouched[c.Id]),
                "a relabel changes the label and nothing else");
    }

    [Fact]
    public async Task Coverage_is_one_on_an_empty_index_and_partial_mid_switch()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        // Nothing to rebuild is full coverage, not zero: a brand-new deployment is not mid-switch.
        (await Reconciler(db, Gemini).RunOnceAsync()).Coverage.Should().Be(1.0);

        SeedExpert(db, summary: "Senior backend engineer.", experiences: 3);
        await Reconciler(db, Gemini).RunOnceAsync();

        // 7 chunks; stop the switch after 3 by failing the fourth batch of one.
        var failing = new FailAfterEmbedder(Azure, allowedBatches: 3);
        await Reconciler(db, failing, batchSize: 1)
            .Invoking(r => r.RunOnceAsync()).Should().ThrowAsync<InvalidOperationException>();

        var coverage = await IndexCoverage.MeasureAsync(db, Azure.Tag, CancellationToken.None);
        coverage.Should().BeApproximately(3d / 7d, 1e-9);
        IndexCoverage.NoteFor(coverage).Should().StartWith("index rebuilding: 43% re-embedded");
    }

    /// <summary>An embedder that dies partway through a rebuild — the case the in-place overwrite
    /// rule exists for.</summary>
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

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static SearchIndexReconciler Reconciler(
        AppDbContext db, IEmbedder? embedder = null, int batchSize = 8) => new(
        db,
        embedder ?? new FakeEmbedder(),
        Options.Create(new SearchIndexOptions { EmbedBatchSize = batchSize }),
        new SearchIndexMetrics(),
        NullLogger<SearchIndexReconciler>.Instance);

    private static Expert SeedExpert(AppDbContext db, string? summary, int experiences)
    {
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = $"ada-{Guid.NewGuid():N}@example.com",
            Summary = summary,
        };

        for (var i = 0; i < experiences; i++)
        {
            expert.Experiences.Add(new Experience
            {
                Id = Guid.NewGuid(),
                Company = $"Company {i}",
                Title = $"Role {i}",
                StartDate = new DateOnly(2020, 1, 1),
                Summary = $"Did meaningful work number {i}.",
                Achievements = [new Achievement { Order = 1, Text = $"Shipped feature {i}." }],
            });
        }

        db.Experts.Add(expert);
        db.SaveChanges();
        return expert;
    }

    /// <summary>Deterministic offline embedder: 1536-dim vector seeded from the text.</summary>
    private sealed class FakeEmbedder : IEmbedder
    {
        public string Model => "fake-embedder";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        {
            var vectors = inputs.Select(Seed).ToList();
            return Task.FromResult(new EmbeddingBatch(vectors, inputs.Count * 5L));
        }

        private static float[] Seed(string text)
        {
            var seed = 17;
            foreach (var c in text)
            {
                seed = unchecked(seed * 31 + c);
            }

            var vector = new float[1536];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = ((seed + i) % 1000) / 1000f;
            }

            return vector;
        }
    }
}
