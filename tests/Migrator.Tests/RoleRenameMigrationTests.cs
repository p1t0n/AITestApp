using System.Data.Common;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Migrator.Tests;

/// <summary>
/// The role rename as a data migration (P1T-236). <c>AppDbContext</c> stores every enum by name, so
/// renaming a <c>UserRole</c> member rewrites rows — and a rename that shipped without this
/// migration would leave every existing account carrying a role string the enum no longer has, which
/// EF surfaces as a failure to materialise a <c>User</c> rather than as a wrong answer.
///
/// <para>Driven by migrating to the previous migration, writing the old strings with raw SQL, and
/// then migrating the rest of the way. Seeding through EF would write the <em>new</em> names and
/// prove nothing; this is the only shape that sees the rewrite happen.</para>
/// </summary>
/// <remarks>
/// pgvector, not stock postgres, for the same reason <see cref="DatabaseBootstrapTests"/> uses it:
/// an earlier migration creates the <c>vector</c> extension.
/// </remarks>
public sealed class RoleRenameMigrationTests : IAsyncLifetime
{
    /// <summary>The migration immediately before the rename — the state a deployed database is in.</summary>
    private const string Before = "20260902101039_AddScoringCandidateContest";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);

    [Fact]
    public async Task Existing_rows_are_rewritten_and_every_session_is_revoked()
    {
        await using var db = Context();
        await db.Database.GetService<IMigrator>().MigrateAsync(Before);
        await InsertLegacyUserAsync(db, "staff@example.com", "ServiceManager", tokenVersion: 4);
        await InsertLegacyUserAsync(db, "bench@example.com", "Expert", tokenVersion: 7);

        await db.Database.MigrateAsync();

        var rows = await ReadRolesAsync(db);
        rows["staff@example.com"].Role.Should().Be("Administrator");
        rows["bench@example.com"].Role.Should().Be("User");

        // Last, and for everybody: the role travels in the session token, so a token minted against
        // a half-renamed row would claim a role the app no longer knows. One bump, everybody signs
        // in again.
        rows["staff@example.com"].TokenVersion.Should().Be(5);
        rows["bench@example.com"].TokenVersion.Should().Be(8);
    }

    [Fact]
    public async Task The_column_default_is_gone()
    {
        // It was 'ServiceManager', and it spoke for rows predating the role split. Keeping it past
        // the rename means a row inserted without a role silently becomes an Administrator — the
        // most privileged account in the system, created by an omission.
        await using var db = Context();
        await db.Database.MigrateAsync();

        var columnDefault = await ScalarAsync(
            db,
            """
            SELECT column_default FROM information_schema.columns
            WHERE table_name = 'Users' AND column_name = 'Role';
            """);

        columnDefault.Should().BeNull();
    }

    [Fact]
    public async Task A_role_the_enum_does_not_have_is_refused_by_the_database()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var write = () => InsertLegacyUserAsync(db, "ghost@example.com", "ServiceManager", tokenVersion: 1);

        // The CHECK is the backstop for the next rename: raw SQL — the e2e seed, a fix-up script —
        // bypasses the enum entirely, and a typo there is otherwise found at sign-in.
        (await write.Should().ThrowAsync<Exception>())
            .Which.GetBaseException().Message.Should().Contain("CK_Users_Role");
    }

    [Fact]
    public async Task Down_puts_the_old_names_back_and_revokes_again()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        await InsertLegacyUserAsync(db, "staff@example.com", "Administrator", tokenVersion: 1);
        await InsertLegacyUserAsync(db, "bench@example.com", "User", tokenVersion: 1);

        await db.Database.GetService<IMigrator>().MigrateAsync(Before);

        var rows = await ReadRolesAsync(db);
        rows["staff@example.com"].Role.Should().Be("ServiceManager");
        rows["bench@example.com"].Role.Should().Be("Expert");

        // Symmetric: rolling back is as much a role change as rolling forward, and the tokens minted
        // in between name roles the rolled-back app does not have.
        rows["staff@example.com"].TokenVersion.Should().Be(2);
        rows["bench@example.com"].TokenVersion.Should().Be(2);
    }

    /// <summary>
    /// An account written the way a deployed database holds one — the role as a literal string,
    /// never through the model, so the test sees what the migration sees.
    /// </summary>
    private static Task InsertLegacyUserAsync(AppDbContext db, string email, string role, int tokenVersion) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "ControlWordHash", "Status", "Role", "TokenVersion",
                                 "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {0}, '', 'Active', {1}, {2}, now(), now());
            """,
            email, role, tokenVersion);

    private static async Task<Dictionary<string, (string Role, int TokenVersion)>> ReadRolesAsync(AppDbContext db)
    {
        var rows = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        await using var command = await CommandAsync(
            db, """SELECT "Email", "Role", "TokenVersion" FROM "Users";""");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows[reader.GetString(0)] = (reader.GetString(1), reader.GetInt32(2));
        }

        return rows;
    }

    private static async Task<object?> ScalarAsync(AppDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<DbCommand> CommandAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
