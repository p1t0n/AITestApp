using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Search;
using EmployeeManager.Infrastructure.Persistence;
using EmployeeManager.Infrastructure.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Entities = EmployeeManager.Domain.Entities;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// Integration tests for the style exemplar retrieval path against real pgvector, using the same
/// deterministic keyword-basis fake embedder as <see cref="ShortlistSearchServiceTests"/> (topical
/// texts land close together; unrelated ones far apart). The chunk index is built by the real
/// reconciler, so achievement bullets flow through the production projection.
/// </summary>
public sealed class ExemplarSearchServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    // Olive's bullets are the query keys; everyone else's are the exemplar pool.
    private Guid _oliveFintechBulletId;
    private Guid _oliveSecondFintechBulletId;
    private Guid _oliveGamingBulletId;
    private Guid _oliveLogisticsBulletId;

    private const string AdaCompanyBullet =
        "Scaled Initech Global Services' fintech ledger to 5M daily transactions.";
    private const string AdaCompanyBulletAnonymized =
        "Scaled [company]' fintech ledger to 5M daily transactions.";
    private const string AdaPlainBullet =
        "Cut fintech onboarding time 40% by automating KYC document checks.";
    private const string CarolBullet =
        "Migrated fintech reporting to stream processing, saving 300 hours annually.";
    private const string GaryBullet =
        "Shipped a gaming physics engine upgrade that lifted frame rates 60% on consoles.";
    private const string BellaUnquantifiedBullet =
        "Substantially improved fintech reliability for large enterprise customers over the years.";

    private static readonly string[] AnonymizedFintechPool =
        [AdaCompanyBulletAnonymized, AdaPlainBullet, CarolBullet];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
        // Build the chunk index (experience + summary + achievement bullet chunks) with the same
        // embedder the search uses.
        await new SearchIndexReconciler(db, new CountingKeywordEmbedder(),
            Options.Create(new SearchIndexOptions()), NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Returns_anonymized_quantified_bullets_from_other_employees_only()
    {
        var result = await Service().SearchAsync([_oliveFintechBulletId], topKPerBullet: 5);

        result.Error.Should().BeNull();
        var group = result.Results.Should().ContainSingle().Subject;
        group.AchievementId.Should().Be(_oliveFintechBulletId);

        // Exactly the three quantified fintech bullets owned by other employees: Olive's own
        // bullets, Bella's unquantified bullet, and every experience/summary chunk are all out.
        group.Exemplars.Select(e => e.Text).Should().BeEquivalentTo(AnonymizedFintechPool);
        group.Exemplars.Should().OnlyContain(e => e.Similarity >= 0.30);

        // The scrub ran: the source company never leaves the service.
        group.Exemplars.Should().NotContain(e => e.Text.Contains("Initech"));
    }

    [Fact]
    public async Task Groups_exemplars_per_requested_bullet_with_two_per_bullet_by_default()
    {
        var result = await Service().SearchAsync([_oliveFintechBulletId, _oliveGamingBulletId]);

        result.Results.Should().HaveCount(2);

        var fintech = result.Results.Single(g => g.AchievementId == _oliveFintechBulletId);
        fintech.Exemplars.Should().HaveCount(2, "ExemplarsPerBullet defaults to 2")
            .And.OnlyContain(e => AnonymizedFintechPool.Contains(e.Text));

        var gaming = result.Results.Single(g => g.AchievementId == _oliveGamingBulletId);
        gaming.Exemplars.Should().ContainSingle().Which.Text.Should().Be(GaryBullet);
    }

    [Fact]
    public async Task The_same_source_bullet_is_never_returned_twice_in_one_request()
    {
        // Both requested bullets are fintech-topical, so they compete for the same pool.
        var result = await Service().SearchAsync(
            [_oliveFintechBulletId, _oliveSecondFintechBulletId], topKPerBullet: 5);

        var texts = result.Results.SelectMany(g => g.Exemplars).Select(e => e.Text).ToList();
        texts.Should().OnlyHaveUniqueItems();
        texts.Should().BeEquivalentTo(AnonymizedFintechPool, "the pool is shared, not repeated per bullet");
    }

    [Fact]
    public async Task A_bullet_with_nothing_above_the_similarity_floor_gets_an_empty_exemplar_set()
    {
        // Nobody else's achievement bullets mention logistics.
        var result = await Service().SearchAsync([_oliveLogisticsBulletId]);

        result.Error.Should().BeNull();
        var group = result.Results.Should().ContainSingle().Subject;
        group.AchievementId.Should().Be(_oliveLogisticsBulletId);
        group.Exemplars.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_achievement_ids_are_skipped_silently()
    {
        var result = await Service().SearchAsync([Guid.NewGuid(), _oliveGamingBulletId]);

        result.Error.Should().BeNull();
        result.Results.Should().ContainSingle().Which.AchievementId.Should().Be(_oliveGamingBulletId);
    }

    [Fact]
    public async Task Only_unknown_ids_returns_empty_without_embedding()
    {
        var embedder = new CountingKeywordEmbedder();

        var result = await Service(embedder).SearchAsync([Guid.NewGuid()]);

        result.Results.Should().BeEmpty();
        result.Error.Should().BeNull();
        embedder.Calls.Should().Be(0);
    }

    [Fact]
    public async Task All_requested_bullets_are_embedded_in_a_single_batched_call()
    {
        var embedder = new CountingKeywordEmbedder();

        await Service(embedder).SearchAsync(
            [_oliveFintechBulletId, _oliveGamingBulletId, _oliveLogisticsBulletId]);

        embedder.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Embedding_failure_returns_a_soft_error_not_an_exception()
    {
        var result = await Service(new ThrowingEmbedder()).SearchAsync([_oliveFintechBulletId]);

        result.Results.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private IExemplarSearchService Service(IEmbedder? embedder = null) => new ExemplarSearchService(
        NewDb(), embedder ?? new CountingKeywordEmbedder(),
        Options.Create(new SemanticSearchOptions()), NullLogger<ExemplarSearchService>.Instance);

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private async Task SeedAsync(AppDbContext db)
    {
        // Olive owns the requested bullets; her own bullets must never come back as exemplars.
        var olive = Employee("Olive", "Owner", "Owncorp",
            "Optimized fintech settlement flows, cutting operating costs 18% year over year.",
            "Rebuilt fintech risk checks to score 10x more events per second at peak.",
            "Led gaming platform tuning, trimming load times by 35% across all titles.",
            "Streamlined logistics scheduling, reducing idle fleet time 22% every quarter.");
        _oliveFintechBulletId = BulletId(olive, 0);
        _oliveSecondFintechBulletId = BulletId(olive, 1);
        _oliveGamingBulletId = BulletId(olive, 2);
        _oliveLogisticsBulletId = BulletId(olive, 3);

        var ada = Employee("Ada", "Lovelace", "Initech Global Services",
            AdaCompanyBullet, AdaPlainBullet);
        var carol = Employee("Carol", "Coder", "Vertex Analytics", CarolBullet);
        var gary = Employee("Gary", "Gamer", "PixelForge", GaryBullet);
        // Bella's bullet is topical but unquantified; her experience summary is topical but is an
        // Experience chunk — neither may surface as an exemplar.
        var bella = Employee("Bella", "Blogs", "Acme", BellaUnquantifiedBullet);
        bella.Experiences.Single().Summary = "Built fintech trading systems.";

        db.Employees.AddRange(olive, ada, carol, gary, bella);
        await db.SaveChangesAsync();
    }

    private static Guid BulletId(Entities.Employee employee, int index)
        => employee.Experiences.Single().Achievements.OrderBy(a => a.Order).ElementAt(index).Id;

    private static Entities.Employee Employee(
        string first, string last, string company, params string[] bullets)
    {
        var experience = new Entities.Experience
        {
            Id = Guid.NewGuid(),
            Company = company,
            Title = "Engineer",
            StartDate = new DateOnly(2020, 1, 1),
            Achievements = bullets
                .Select((text, i) => new Entities.Achievement { Id = Guid.NewGuid(), Order = i, Text = text })
                .ToList(),
        };
        return new Entities.Employee
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Title = "Engineer",
            Location = "London",
            Email = $"{first}-{Guid.NewGuid():N}@example.com".ToLower(),
            Experiences = [experience],
        };
    }

    /// <summary>Topical fake embedder (see <see cref="SemanticSearchServiceTests"/>) that also
    /// counts EmbedAsync calls, so tests can assert bullets are embedded in one batch.</summary>
    private sealed class CountingKeywordEmbedder : IEmbedder
    {
        private static readonly string[] Vocab = ["fintech", "gaming", "logistics", "payments"];

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
