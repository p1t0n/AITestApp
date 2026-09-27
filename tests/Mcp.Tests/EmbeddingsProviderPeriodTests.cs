using ExpertToJob.Application.Compliance;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The embeddings provider history (EXP-66, <c>manuals/adr-embeddings-provider-seam.md</c> §2
/// decision 17). <b>Append-only, and it holds no personal data</b> — a provider name and two
/// timestamps per deployment, never an Expert id — which is why it can be read by the access view
/// for every data subject without becoming a store erasure has to reach.
///
/// <para>Against real Postgres rather than EF InMemory, because the seeded row is written by a
/// <em>migration</em>: an in-memory model would create the table from the model and see nothing
/// in it, so the one assertion that matters most here would pass by being unable to fail.</para>
/// </summary>
public sealed class EmbeddingsProviderPeriodTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// "Since the beginning" is an <em>empty</em> start date, not today's date: every deployment
    /// that existed before this table did was on Gemini from its first row, and stamping the
    /// migration's own run time would claim a start we cannot know.
    /// </summary>
    [Fact]
    public async Task Migration_seeds_an_open_Gemini_period_since_the_beginning()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        var periods = await db.EmbeddingsProviderPeriods.AsNoTracking().ToListAsync();

        periods.Should().ContainSingle();
        periods[0].Provider.Should().Be("Gemini");
        periods[0].StartedAt.Should().BeNull("the row means 'since the beginning'");
        periods[0].EndedAt.Should().BeNull("Gemini is still the provider until a host says otherwise");
    }

    [Fact]
    public async Task Startup_with_the_same_provider_writes_nothing()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        await HistoryOver(db).RecordActiveProviderAsync("Gemini");

        var periods = await NewDb().EmbeddingsProviderPeriods.AsNoTracking().ToListAsync();
        periods.Should().ContainSingle("nothing changed, so nothing is written");
        periods[0].EndedAt.Should().BeNull();
        periods[0].StartedAt.Should().BeNull("the seeded row is left exactly as it was");
    }

    [Fact]
    public async Task Startup_opens_a_new_period_when_the_provider_changes()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        await HistoryOver(db).RecordActiveProviderAsync("AzureFoundry");

        var periods = await NewDb().EmbeddingsProviderPeriods.AsNoTracking()
            .OrderBy(p => p.Provider).ToListAsync();

        periods.Should().HaveCount(2);
        var gemini = periods.Single(p => p.Provider == "Gemini");
        var azure = periods.Single(p => p.Provider == "AzureFoundry");

        gemini.EndedAt.Should().Be(SwitchedAt, "the old period closes at the moment the new one opens");
        azure.StartedAt.Should().Be(SwitchedAt);
        azure.EndedAt.Should().BeNull();
    }

    /// <summary>
    /// The second start under the same provider is the ordinary case — every restart runs this —
    /// so it has to be idempotent against the row its own first run wrote, not only against the
    /// migration's seed.
    /// </summary>
    [Fact]
    public async Task A_restart_after_a_switch_writes_nothing_more()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        await HistoryOver(db).RecordActiveProviderAsync("AzureFoundry");
        await HistoryOver(NewDb()).RecordActiveProviderAsync("AzureFoundry");

        (await NewDb().EmbeddingsProviderPeriods.CountAsync()).Should().Be(2);
    }

    /// <summary>What the access view reads: the whole history, oldest first.</summary>
    [Fact]
    public async Task The_history_reads_back_oldest_first()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        await HistoryOver(db).RecordActiveProviderAsync("AzureFoundry");

        var periods = await HistoryOver(NewDb()).PeriodsAsync();

        periods.Select(p => p.Provider).Should().Equal("Gemini", "AzureFoundry");
    }

    private static readonly DateTimeOffset SwitchedAt =
        new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    private static EmbeddingsProviderHistory HistoryOver(AppDbContext db) =>
        new(db, new FixedClock(SwitchedAt));

    /// <summary>A clock rather than a package: the whole need here is one stated instant.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseVector())
            .Options);
}
