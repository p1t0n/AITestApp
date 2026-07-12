using EmployeeManager.Application.Abstractions;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using EmployeeManager.Infrastructure.Persistence;
using EmployeeManager.Infrastructure.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// Integration tests for <see cref="SearchIndexReconciler"/> against a real pgvector Postgres
/// (Testcontainers). Uses a deterministic fake embedder — the pgvector column, migrations, and the
/// reconcile/backfill loop are what's under test, not the embedding provider.
/// </summary>
public sealed class SearchIndexReconcilerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task First_pass_backfills_all_chunks_with_embeddings()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var employee = SeedEmployee(db, summary: "Senior backend engineer.", experiences: 2);

        var report = await Reconciler(db).RunOnceAsync();

        // 1 summary + 2 experience + 2 achievement-bullet chunks, all embedded on the first
        // (backfill) pass.
        report.Inserted.Should().Be(5);
        report.Embedded.Should().Be(5);
        report.EmbeddingTokens.Should().BeGreaterThan(0);

        var chunks = await db.EmployeeSearchChunks.Where(c => c.EmployeeId == employee.Id).ToListAsync();
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
        SeedEmployee(db, summary: "Bio.", experiences: 1);

        await Reconciler(db).RunOnceAsync();
        var second = await Reconciler(db).RunOnceAsync();

        second.DidWork.Should().BeFalse();
    }

    [Fact]
    public async Task Editing_one_experience_re_embeds_only_that_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var employee = SeedEmployee(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var edited = employee.Experiences.First();
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
        var employee = SeedEmployee(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var toRemove = employee.Experiences.First();
        db.Experiences.Remove(toRemove);
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        // The experience chunk and its cascaded achievement's bullet chunk both go.
        report.Deleted.Should().Be(2);
        (await db.EmployeeSearchChunks.CountAsync(c => c.SourceId == toRemove.Id)).Should().Be(0);
        var orphanedAchievementIds = toRemove.Achievements.Select(a => a.Id).ToList();
        (await db.EmployeeSearchChunks.CountAsync(c => orphanedAchievementIds.Contains(c.SourceId))).Should().Be(0);
    }

    [Fact]
    public async Task Editing_a_bullet_re_embeds_its_chunk_and_the_parent_experience_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var employee = SeedEmployee(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        var parent = employee.Experiences.First();
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

        var bulletChunk = await db.EmployeeSearchChunks.SingleAsync(c => c.SourceId == edited.Id);
        bulletChunk.Content.Should().Be("Shipped something entirely different.");
        bulletChunk.Embedding.Should().NotBeNull();
    }

    [Fact]
    public async Task Deleting_an_achievement_removes_its_chunk_and_updates_the_experience_chunk()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var employee = SeedEmployee(db, summary: "Bio.", experiences: 1);
        await Reconciler(db).RunOnceAsync();

        var doomed = employee.Experiences.Single().Achievements.Single();
        db.Achievements.Remove(doomed);
        await db.SaveChangesAsync();

        var report = await Reconciler(db).RunOnceAsync();

        // The bullet chunk is orphaned; the parent experience chunk re-renders without the bullet.
        report.Deleted.Should().Be(1);
        report.Updated.Should().Be(1);
        (await db.EmployeeSearchChunks.CountAsync(c => c.SourceId == doomed.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_an_employee_cascades_away_its_chunks()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        var employee = SeedEmployee(db, summary: "Bio.", experiences: 2);
        await Reconciler(db).RunOnceAsync();

        db.Employees.Remove(await db.Employees.FirstAsync(e => e.Id == employee.Id));
        await db.SaveChangesAsync();

        (await db.EmployeeSearchChunks.CountAsync(c => c.EmployeeId == employee.Id)).Should().Be(0);
    }

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static SearchIndexReconciler Reconciler(AppDbContext db) => new(
        db,
        new FakeEmbedder(),
        Options.Create(new SearchIndexOptions { EmbedBatchSize = 8 }),
        NullLogger<SearchIndexReconciler>.Instance);

    private static Employee SeedEmployee(AppDbContext db, string? summary, int experiences)
    {
        var employee = new Employee
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
            employee.Experiences.Add(new Experience
            {
                Id = Guid.NewGuid(),
                Company = $"Company {i}",
                Title = $"Role {i}",
                StartDate = new DateOnly(2020, 1, 1),
                Summary = $"Did meaningful work number {i}.",
                Achievements = [new Achievement { Order = 1, Text = $"Shipped feature {i}." }],
            });
        }

        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
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
