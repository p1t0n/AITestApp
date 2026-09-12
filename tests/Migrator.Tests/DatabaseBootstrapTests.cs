using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Migrator;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Migrator.Tests;

/// <summary>
/// The migrator against a real, empty database (P1T-215). This is the assertion the slice exists
/// for: a database nothing has touched becomes a complete schema plus a seeded catalog, without any
/// host having started. Everything downstream — the MCP server booting first, the e2e runner, the
/// AppHost's <c>WaitForCompletion(…, exitCode: 0)</c> — rests on it.
/// </summary>
/// <remarks>
/// pgvector, not stock postgres: the <c>AddExpertSearchChunk</c> migration creates the
/// <c>vector</c> extension, so a plain image fails to migrate and would make this test lie.
/// </remarks>
public sealed class DatabaseBootstrapTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext Context(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString ?? _postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);

    [Fact]
    public async Task An_empty_database_gets_the_whole_schema_and_the_seeded_catalog()
    {
        await using var db = Context();

        var exitCode = await DatabaseBootstrap.RunAsync(db, NullLogger.Instance);

        exitCode.Should().Be(0);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
            "the migrator leaves nothing for the next process to apply");
        (await db.Categories.CountAsync()).Should().BeGreaterThan(0);
        (await db.Skills.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Running_it_twice_changes_nothing_and_still_succeeds()
    {
        // The AppHost will run this on every start, and a developer will run it by hand after a
        // pull. Both have to be free.
        await using var first = Context();
        (await DatabaseBootstrap.RunAsync(first, NullLogger.Instance)).Should().Be(0);
        var categories = await first.Categories.CountAsync();

        await using var second = Context();
        (await DatabaseBootstrap.RunAsync(second, NullLogger.Instance)).Should().Be(0);

        (await second.Categories.CountAsync()).Should().Be(categories);
    }

    [Fact]
    public async Task A_database_it_cannot_reach_is_a_non_zero_exit_not_an_exception()
    {
        // Port 1 answers nothing. The exit code is the contract: `WaitForCompletion(…, exitCode: 0)`
        // and the e2e runner both read it, and a thrown exception would make the process fail in a
        // way neither of them can distinguish from a crash.
        await using var db = Context("Host=localhost;Port=1;Database=nothing;Username=postgres;Password=postgres");

        var exitCode = await DatabaseBootstrap.RunAsync(db, NullLogger.Instance);

        exitCode.Should().NotBe(0);
    }
}
