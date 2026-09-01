using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Search;
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
/// Integration tests for <see cref="SemanticSearchService"/> against real pgvector. A keyword-based
/// fake embedder makes similarity deterministic: a chunk mentioning a topic embeds near a query for
/// that topic, and unrelated chunks fall below the similarity threshold.
/// </summary>
public sealed class SemanticSearchServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    private Guid _reactSkillId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
        // Backfill the index with the same embedder the search uses.
        await new SearchIndexReconciler(db, new KeywordEmbedder(),
            Options.Create(new SearchIndexOptions()), NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync();
        await SeedLarrysBulletChunkAsync(db);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Ranks_topically_relevant_employees_and_excludes_others()
    {
        var result = await Service().SearchAsync("fintech");

        result.Error.Should().BeNull();
        result.Results.Select(r => r.Name)
            .Should().Contain(["Fiona Fintech", "Pat Payments"])
            .And.NotContain("Gary Gaming");
        result.Results.Should().OnlyContain(r => r.Score >= 0.30);
        result.Results[0].Snippets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Achievement_bullet_chunks_are_excluded_from_the_retrieval_pool()
    {
        // Larry's only "logistics" mention lives in an achievement bullet; the employee-level
        // search must not surface him through it (bullet chunks are reserved for the exemplar
        // retrieval path).
        var result = await Service().SearchAsync("logistics");

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Off_topic_query_returns_empty_rather_than_least_bad()
    {
        var result = await Service().SearchAsync("logistics");

        result.Results.Should().BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task Location_pre_filter_excludes_valid_topical_matches_elsewhere()
    {
        // Fiona (fintech) is in London; filtering to Berlin leaves only Gary (gaming) eligible,
        // who is off-topic for "fintech" -> no hits.
        var result = await Service().SearchAsync("fintech", new SemanticSearchFilters(Location: "Berlin"));

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Skill_pre_filter_keeps_only_employees_with_the_skill()
    {
        // Only Fiona has React among the fintech people.
        var result = await Service().SearchAsync("fintech",
            new SemanticSearchFilters(SkillIds: [_reactSkillId]));

        result.Results.Should().ContainSingle().Which.Name.Should().Be("Fiona Fintech");
    }

    [Fact]
    public async Task Embedding_failure_returns_a_soft_error_not_an_exception()
    {
        await using var db = NewDb();
        var service = new SemanticSearchService(db, new ThrowingEmbedder(),
            Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

        var result = await service.SearchAsync("fintech");

        result.Results.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private SemanticSearchService Service() => new(
        NewDb(), new KeywordEmbedder(),
        Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private async Task SeedAsync(AppDbContext db)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Frontend" };
        var react = new Skill { Id = Guid.NewGuid(), Name = "React", Category = category, CategoryId = category.Id };
        _reactSkillId = react.Id;
        db.Categories.Add(category);
        db.Skills.Add(react);

        var fiona = Employee("Fiona", "Fintech", "London", "Built fintech trading systems.");
        fiona.Skills.Add(new EmployeeSkill
        {
            Id = Guid.NewGuid(), SkillId = react.Id, Level = SkillLevel.Advanced, YearsExperience = 5m,
        });

        var pat = Employee("Pat", "Payments", "London", "Ran a fintech payments platform.");
        var gary = Employee("Gary", "Gaming", "Berlin", "Wrote gaming engines.");

        db.Employees.AddRange(fiona, pat, gary);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Insert an embedded Achievement chunk row for Larry directly (after the reconciler pass, so
    /// it survives). Seeding via the projection would also put the bullet's keyword into the parent
    /// experience chunk, which would hide whether the bullet chunk itself is in the pool.
    /// </summary>
    private async Task SeedLarrysBulletChunkAsync(AppDbContext db)
    {
        var larry = Employee("Larry", "Logistics", "London", "Moved boxes around.");
        larry.Experiences.Clear();
        db.Employees.Add(larry);

        var embedded = await new KeywordEmbedder().EmbedAsync(["Optimized logistics routing."]);
        db.EmployeeSearchChunks.Add(new EmployeeSearchChunk
        {
            Id = Guid.NewGuid(),
            EmployeeId = larry.Id,
            SourceType = SearchChunkSource.Achievement,
            SourceId = Guid.NewGuid(),
            Content = "Optimized logistics routing.",
            ContentHash = ChunkProjection.Hash("Optimized logistics routing."),
            Embedding = new Pgvector.Vector(embedded.Vectors[0]),
            Model = "keyword-embedder",
            EmbeddedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static Employee Employee(string first, string last, string location, string experienceSummary) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = first,
        LastName = last,
        Title = "Engineer",
        Location = location,
        Email = $"{first}-{Guid.NewGuid():N}@example.com".ToLower(),
        Experiences =
        [
            new Experience
            {
                Id = Guid.NewGuid(),
                Company = "Acme",
                Title = "Engineer",
                StartDate = new DateOnly(2020, 1, 1),
                Summary = experienceSummary,
            },
        ],
    };

    /// <summary>Topical fake embedder: a small keyword vocabulary maps to basis dimensions, plus a
    /// tiny baseline so no vector is all-zero (pgvector cosine distance is undefined for that).</summary>
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

    private sealed class ThrowingEmbedder : IEmbedder
    {
        public string Model => "throwing";
        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => throw new InvalidOperationException("backend down");
    }

    [Fact]
    public async Task Quota_exhaustion_falls_back_to_keyword_search_flagged_degraded()
    {
        await using var db = NewDb();
        var service = new SemanticSearchService(db, new QuotaDeadEmbedder(),
            Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

        var result = await service.SearchAsync("fintech");

        result.Error.Should().BeNull();
        result.DegradedReason.Should().NotBeNullOrWhiteSpace();
        result.Results.Select(r => r.Name)
            .Should().Contain(["Fiona Fintech", "Pat Payments"])
            .And.NotContain("Gary Gaming");
        result.Results[0].Snippets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Degraded_fallback_respects_filters_and_chunk_pool_rules()
    {
        await using var db = NewDb();
        var service = new SemanticSearchService(db, new QuotaDeadEmbedder(),
            Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

        // Hard filters still apply: only Gary is in Berlin, and he is off-topic for "fintech".
        var filtered = await service.SearchAsync("fintech", new SemanticSearchFilters(Location: "Berlin"));
        filtered.Results.Should().BeEmpty();

        // Achievement bullet chunks stay out of the employee-level pool, same as the semantic path.
        var bullets = await service.SearchAsync("logistics");
        bullets.Results.Should().BeEmpty();
    }

    private sealed class QuotaDeadEmbedder : IEmbedder
    {
        public string Model => "quota-dead";
        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => throw new EmbeddingQuotaExceededException("daily quota exhausted");
    }
}
