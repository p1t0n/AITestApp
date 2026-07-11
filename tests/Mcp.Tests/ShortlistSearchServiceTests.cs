using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Search;
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
/// Integration tests for the shortlist (multi-requirement) search path against real pgvector.
/// The same keyword-basis fake embedder as <see cref="SemanticSearchServiceTests"/> makes
/// similarity deterministic; here it also counts calls so we can assert requirements are embedded
/// in a single batch.
/// </summary>
public sealed class ShortlistSearchServiceTests : IAsyncLifetime
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
        await new SearchIndexReconciler(db, new CountingKeywordEmbedder(),
            Options.Create(new SearchIndexOptions()), NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Ranks_broad_coverage_above_single_requirement_matches()
    {
        var result = await Service().SearchAsync(["fintech", "gaming"]);

        result.Error.Should().BeNull();
        // Bella matches both requirements; Fiona and Gary each match one.
        result.Results.Select(r => r.Name)
            .Should().HaveCount(3).And.StartWith("Bella Both");
        var bella = result.Results[0];
        bella.MatchedCount.Should().Be(2);
        bella.TotalRequirements.Should().Be(2);
        bella.Score.Should().BeGreaterThan(result.Results[1].Score);
        bella.Evidence.Should().OnlyContain(e => e.Matched && e.Snippet != null && e.Similarity >= 0.30);
    }

    [Fact]
    public async Task Skill_pre_filter_excludes_an_otherwise_matching_candidate()
    {
        // Bella matches "fintech" topically but lacks React; only Fiona survives the pre-filter.
        var result = await Service().SearchAsync(["fintech"],
            new SemanticSearchFilters(SkillIds: [_reactSkillId]));

        result.Results.Should().ContainSingle().Which.Name.Should().Be("Fiona Fintech");
    }

    [Fact]
    public async Task Sub_threshold_requirement_counts_as_missed_with_no_snippet()
    {
        // Nobody's narrative is about logistics, so that requirement is below MinSimilarity for all.
        var result = await Service().SearchAsync(["fintech", "logistics"]);

        result.Results.Should().NotBeEmpty();
        var fiona = result.Results.Single(r => r.Name == "Fiona Fintech");
        fiona.MatchedCount.Should().Be(1);
        var missed = fiona.Evidence.Single(e => e.Requirement == "logistics");
        missed.Matched.Should().BeFalse();
        missed.Snippet.Should().BeNull();
        missed.Similarity.Should().BeNull();
    }

    [Fact]
    public async Task All_requirements_are_embedded_in_a_single_batched_call()
    {
        var embedder = new CountingKeywordEmbedder();

        await Service(embedder).SearchAsync(["fintech", "gaming", "payments"]);

        embedder.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Whitespace_only_requirements_return_empty_without_embedding()
    {
        var embedder = new CountingKeywordEmbedder();

        var result = await Service(embedder).SearchAsync(["", "   "]);

        result.Results.Should().BeEmpty();
        result.Error.Should().BeNull();
        embedder.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Embedding_failure_returns_a_soft_error_not_an_exception()
    {
        var result = await Service(new ThrowingEmbedder()).SearchAsync(["fintech", "gaming"]);

        result.Results.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private IShortlistSearchService Service(IEmbedder? embedder = null) => new SemanticSearchService(
        NewDb(), embedder ?? new CountingKeywordEmbedder(),
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

        var bella = Employee("Bella", "Both", "London",
            "Built fintech trading systems.", "Wrote gaming engines.");

        var fiona = Employee("Fiona", "Fintech", "London", "Built fintech trading systems.");
        fiona.Skills.Add(new EmployeeSkill
        {
            Id = Guid.NewGuid(), SkillId = react.Id, Level = SkillLevel.Advanced, YearsExperience = 5m,
        });

        var gary = Employee("Gary", "Gaming", "Berlin", "Wrote gaming engines.");

        db.Employees.AddRange(bella, fiona, gary);
        await db.SaveChangesAsync();
    }

    private static Employee Employee(string first, string last, string location, params string[] experienceSummaries) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = first,
        LastName = last,
        Title = "Engineer",
        Location = location,
        Email = $"{first}-{Guid.NewGuid():N}@example.com".ToLower(),
        Experiences = experienceSummaries
            .Select(summary => new Experience
            {
                Id = Guid.NewGuid(),
                Company = "Acme",
                Title = "Engineer",
                StartDate = new DateOnly(2020, 1, 1),
                Summary = summary,
            })
            .ToList(),
    };

    /// <summary>Topical fake embedder (see <see cref="SemanticSearchServiceTests"/>) that also
    /// counts EmbedAsync calls, so tests can assert requirements are embedded in one batch.</summary>
    private sealed class CountingKeywordEmbedder : IEmbedder
    {
        private static readonly string[] Vocab = ["fintech", "gaming", "payments", "logistics"];

        public int Calls { get; private set; }

        public string Model => "keyword-embedder";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new EmbeddingBatch(inputs.Select(Vectorize).ToList(), inputs.Count));
        }

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
}
