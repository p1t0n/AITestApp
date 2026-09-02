using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Search;
using ExpertToJob.Application.Visibility;
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
/// The visibility seam against real pgvector (P1T-185). Everything here is a database-truth
/// question — the partial indexes, the correlated subqueries, and above all whether a paused
/// Expert's <em>vectors still sitting in the table</em> can surface them. EF InMemory has no
/// indexes and no vector type, so asserting any of it there would assert nothing.
///
/// <para>Two predicates ride one seam: <c>HiddenAt</c> (the person paused themselves) and the
/// Art. 22 route (only a row on 6(1)(b) may be enumerated for scoring). They are tested together
/// because they ship together, and because the interesting failures are the ones where a row passes
/// one and not the other.</para>
/// </summary>
public sealed class RosterVisibilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    private Guid _fionaId;
    private Guid _patId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
        await new SearchIndexReconciler(db, new KeywordEmbedder(),
                Options.Create(new SearchIndexOptions()), NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ---- The retrieval assertion, and the reason this file exists ------------------------------

    /// <summary>
    /// The one most likely to regress silently: the chunks and the embeddings are right there in the
    /// table, and nothing but a query predicate keeps the paused person out of the ranking. They are
    /// kept rather than deleted on purpose — re-embedding on unhide would spend the 100/day quota to
    /// undo something reversible, so a pause must not cost a paid resource.
    /// </summary>
    [Fact]
    public async Task A_paused_expert_cannot_surface_from_semantic_search_though_their_vectors_remain()
    {
        (await Search().SearchAsync("fintech")).Results.Select(r => r.Name)
            .Should().Contain("Fiona Fintech", "she is on the bench before the pause");

        await PauseAsync(_fionaId);

        var after = await Search().SearchAsync("fintech");
        after.Results.Select(r => r.Name).Should().NotContain("Fiona Fintech").And.Contain("Pat Payments");

        await using var db = NewDb();
        var chunks = await db.ExpertSearchChunks.AsNoTracking()
            .Where(c => c.ExpertId == _fionaId).ToListAsync();

        chunks.Should().NotBeEmpty("a pause deletes nothing");
        chunks.Should().Contain(c => c.Embedding != null, "and the vectors are still embedded");
    }

    [Fact]
    public async Task A_paused_expert_cannot_surface_from_shortlist_search()
    {
        await PauseAsync(_fionaId);

        var result = await Search().SearchAsync(["fintech trading systems"]);

        result.Results.Select(c => c.Name).Should().NotContain("Fiona Fintech");
    }

    /// <summary>
    /// The quota fallback is a second, entirely separate retrieval path over the same chunk pool —
    /// Postgres full-text rather than vectors — and it is the one a reader forgets exists.
    /// </summary>
    [Fact]
    public async Task A_paused_expert_cannot_surface_from_the_lexical_fallback_either()
    {
        await PauseAsync(_fionaId);

        await using var db = NewDb();
        var degraded = new SemanticSearchService(db, new QuotaDeadEmbedder(),
            Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

        var result = await degraded.SearchAsync("fintech");

        result.DegradedReason.Should().NotBeNullOrWhiteSpace("this is the fallback path, not the vector one");
        result.Results.Select(r => r.Name).Should().NotContain("Fiona Fintech");
    }

    /// <summary>
    /// The pause is free, and this is what "free" means concretely: nothing was deleted, so nothing
    /// is re-embedded, so no quota is spent putting somebody back on the bench. Asserted on the
    /// embedding timestamps rather than on a count, because a delete-and-recreate cycle would keep
    /// the count identical while spending the quota twice.
    /// </summary>
    [Fact]
    public async Task Unhiding_costs_no_embeddings()
    {
        var before = await EmbeddingStampsAsync(_fionaId);

        await PauseAsync(_fionaId);
        await ResumeAsync(_fionaId);

        (await Search().SearchAsync("fintech")).Results.Select(r => r.Name)
            .Should().Contain("Fiona Fintech", "the pause is reversible and complete");

        var after = await EmbeddingStampsAsync(_fionaId);
        after.Should().BeEquivalentTo(before, "not one chunk was re-embedded");
    }

    [Fact]
    public async Task A_paused_expert_is_not_quoted_as_a_style_exemplar()
    {
        await using var seed = NewDb();
        await SeedBulletAsync(seed, _patId, "Cut fintech settlement latency by 40% across 12 markets.");

        var visible = await Exemplars().SearchAsync(null, theme: "fintech");
        visible.ThemeResult!.Exemplars.Should().NotBeEmpty(
            "Pat's bullet is quotable while he is on the bench");

        await PauseAsync(_patId);

        var paused = await Exemplars().SearchAsync(null, theme: "fintech");
        (paused.ThemeResult?.Exemplars ?? []).Should().BeEmpty(
            "anonymised or not, it is still a paused person's own writing being put to work");
    }

    // ---- The Art. 22 route ----------------------------------------------------------------------

    /// <summary>
    /// Shared with P1T-179 and load-bearing: legitimate interest is not among the three Art. 22(2)
    /// exceptions, so a row on it has no route to automated decision-making and the scan must not
    /// enumerate it at all. Scoring-without-persisting was rejected — the model call is the
    /// processing.
    /// </summary>
    [Fact]
    public async Task A_basis_transition_turns_scannability_on_and_off()
    {
        await using var db = NewDb();
        var digests = new ExpertDigestService(db);

        var unclaimed = await SeedExpertAsync(db, "Unclaimed", "Ulric", "London",
            "Ran a fintech payments platform.", claimed: false);

        (await digests.ListAsync(pageSize: 100)).Items.Select(d => d.ExpertId)
            .Should().NotContain(unclaimed, "an unclaimed bench member is not scanned");

        // Approve a claim: LI → 6(1)(b), and the row becomes scannable.
        await AppendBasisAsync(db, unclaimed, ProcessingOrigin.SelfRegistered, "Claim approved.");
        (await digests.ListAsync(pageSize: 100)).Items.Select(d => d.ExpertId)
            .Should().Contain(unclaimed);

        // Revoke: a new record back to LI — never a rewrite — and they stop being scanned.
        await AppendBasisAsync(db, unclaimed, ProcessingOrigin.StaffCreated, "Ownership revoked.");
        (await digests.ListAsync(pageSize: 100)).Items.Select(d => d.ExpertId)
            .Should().NotContain(unclaimed);

        var history = await db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == unclaimed).OrderBy(r => r.Sequence).ToListAsync();
        history.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity, LawfulBasis.LegitimateInterest);
    }

    /// <summary>
    /// The scan and retrieval do not share a predicate set, and this is the pair that proves it: a
    /// claimed row that paused itself is out of both, while an unclaimed row is out of the scan and
    /// still findable by a Service Manager searching the bench.
    /// </summary>
    [Fact]
    public async Task The_two_predicates_are_not_the_same_predicate()
    {
        await using var db = NewDb();

        // Fiona is claimed and on the bench: in both populations.
        (await db.Experts.Scannable().Select(e => e.Id).ToListAsync()).Should().Contain(_fionaId);
        (await db.Experts.OnTheBench().Select(e => e.Id).ToListAsync()).Should().Contain(_fionaId);

        var unclaimed = await SeedExpertAsync(db, "Bench", "Member", "London",
            "Ran a fintech payments platform.", claimed: false);

        (await db.Experts.Scannable().Select(e => e.Id).ToListAsync())
            .Should().NotContain(unclaimed, "no Art. 22(2) route");
        (await db.Experts.OnTheBench().Select(e => e.Id).ToListAsync())
            .Should().Contain(unclaimed, "but still on the bench, and still searchable");
    }

    // ---- The index the claim rule depends on ------------------------------------------------------

    /// <summary>
    /// A hidden Expert keeps <c>Status = Active</c>, which is the whole reason
    /// <c>HiddenAt</c> is a timestamp and not a third <see cref="ExpertStatus"/> value. If pausing
    /// quietly moved a row out of the partial unique index, P1T-184's claim-matching rule would
    /// change meaning underneath it — and nothing would say so.
    /// </summary>
    [Fact]
    public async Task Pausing_leaves_the_active_email_uniqueness_rule_exactly_as_it_was()
    {
        await using var db = NewDb();
        var email = $"twin-{Guid.NewGuid():N}@example.com";
        var first = await SeedExpertAsync(db, "First", "Twin", "London", "Ran payments.", email: email);

        await PauseAsync(first);

        await using var other = NewDb();
        other.Experts.Add(new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Second",
            LastName = "Twin",
            Title = "Engineer",
            Email = email,
            Status = ExpertStatus.Active,
        });

        var clash = async () => await other.SaveChangesAsync();
        await clash.Should().ThrowAsync<DbUpdateException>(
            "the paused row is still Active, so it still holds the address");

        (await db.Experts.AsNoTracking().SingleAsync(e => e.Id == first))
            .Status.Should().Be(ExpertStatus.Active);
    }

    // ---- Harness ------------------------------------------------------------------------------------

    private SemanticSearchService Search() => new(
        NewDb(), new KeywordEmbedder(),
        Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

    private ExemplarSearchService Exemplars() => new(
        NewDb(), new KeywordEmbedder(),
        Options.Create(new SemanticSearchOptions()), NullLogger<ExemplarSearchService>.Instance);

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    private async Task PauseAsync(Guid expertId) => await SetHiddenAsync(expertId, DateTimeOffset.UtcNow);

    private async Task ResumeAsync(Guid expertId) => await SetHiddenAsync(expertId, null);

    private async Task SetHiddenAsync(Guid expertId, DateTimeOffset? at)
    {
        await using var db = NewDb();
        (await db.Experts.SingleAsync(e => e.Id == expertId)).HiddenAt = at;
        await db.SaveChangesAsync();
    }

    private async Task<List<DateTimeOffset?>> EmbeddingStampsAsync(Guid expertId)
    {
        await using var db = NewDb();
        return await db.ExpertSearchChunks.AsNoTracking()
            .Where(c => c.ExpertId == expertId)
            .OrderBy(c => c.SourceId)
            .Select(c => c.EmbeddedAt)
            .ToListAsync();
    }

    private async Task SeedAsync(AppDbContext db)
    {
        _fionaId = await SeedExpertAsync(db, "Fiona", "Fintech", "London", "Built fintech trading systems.");
        _patId = await SeedExpertAsync(db, "Pat", "Payments", "London", "Ran a fintech payments platform.");
    }

    /// <summary>
    /// One bench row. <paramref name="claimed"/> decides its lawful basis, which is what decides
    /// whether the scan may enumerate it at all — seeded rows are staff-created and therefore on
    /// legitimate interest, exactly as the real seeders write them.
    /// </summary>
    private static async Task<Guid> SeedExpertAsync(
        AppDbContext db, string first, string last, string location, string summary,
        bool claimed = true, string? email = null)
    {
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Title = "Engineer",
            Email = email ?? $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            Location = location,
            Summary = summary,
            Status = ExpertStatus.Active,
        };
        expert.ProcessingRecords.Add(ProcessingRecord.For(
            expert.Id, 1,
            claimed ? ProcessingOrigin.SelfRegistered : ProcessingOrigin.StaffCreated,
            claimed ? TransparencyNotice.CurrentVersion : null,
            claimed ? "Registered and asked to be considered." : "Added to the bench by a Service Manager.",
            DateTimeOffset.UtcNow));

        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    private static async Task AppendBasisAsync(
        AppDbContext db, Guid expertId, ProcessingOrigin origin, string reason)
    {
        var sequence = await db.ProcessingRecords.CountAsync(r => r.ExpertId == expertId);
        db.ProcessingRecords.Add(ProcessingRecord.For(
            expertId, sequence + 1, origin,
            origin == ProcessingOrigin.SelfRegistered ? TransparencyNotice.CurrentVersion : null,
            reason, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>Topical fake embedder, mirroring the one in <c>SemanticSearchServiceTests</c>: a
    /// small keyword vocabulary maps to basis dimensions, plus a tiny baseline so no vector is
    /// all-zero (pgvector cosine distance is undefined for that).</summary>
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
            v[1000] = 0.01f;
            for (var i = 0; i < Vocab.Length; i++)
            {
                if (lower.Contains(Vocab[i])) { v[i] = 1f; }
            }

            return v;
        }
    }

    /// <summary>An embedder whose quota is gone, which is what drives the lexical fallback.</summary>
    private sealed class QuotaDeadEmbedder : IEmbedder
    {
        public string Model => "quota-dead";

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
            => throw new EmbeddingQuotaExceededException("quota exhausted");
    }

    /// <summary>An embedded achievement bullet, inserted after the reconciler pass so it survives —
    /// the exemplar pool is achievement chunks and nothing else.</summary>
    private static async Task<Guid> SeedBulletAsync(AppDbContext db, Guid expertId, string text)
    {
        var experience = new Experience
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            Company = "FlowWorks",
            Title = "Platform Lead",
            StartDate = new DateOnly(2019, 3, 1),
        };
        var achievement = new Achievement { Id = Guid.NewGuid(), ExperienceId = experience.Id, Text = text, Order = 1 };
        experience.Achievements.Add(achievement);
        db.Experiences.Add(experience);

        var embedded = await new KeywordEmbedder().EmbedAsync([text]);
        db.ExpertSearchChunks.Add(new ExpertSearchChunk
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            SourceType = SearchChunkSource.Achievement,
            SourceId = achievement.Id,
            Content = text,
            ContentHash = ChunkProjection.Hash(text),
            Embedding = new Pgvector.Vector(embedded.Vectors[0]),
            Model = "keyword-embedder",
            EmbeddedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return achievement.Id;
    }
}
