using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Migrator;

/// <summary>
/// Everything this process does: apply the pending migrations, seed the skill catalog and the
/// sample experts, report an exit code.
/// </summary>
/// <remarks>
/// Separate from <c>Program.cs</c> so it can be run against a real database in a test
/// (<c>tests/Migrator.Tests</c>) rather than only by starting a process and believing the result.
/// </remarks>
public static class DatabaseBootstrap
{
    /// <summary>The schema is applied and the catalog is seeded.</summary>
    public const int Ok = 0;

    /// <summary>Nothing downstream should start: the database is not in a known state.</summary>
    public const int Failed = 1;

    /// <summary>
    /// Runs to completion and returns an exit code rather than throwing, because the exit code is
    /// the contract every caller reads — the AppHost's <c>WaitForCompletion(…, exitCode: 0)</c>,
    /// the e2e runner, and a developer's shell.
    /// </summary>
    public static async Task<int> RunAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await db.Database.MigrateAsync(ct);
            await DbInitializer.SeedAsync(db, ct);

            logger.LogInformation("Database ready: migrations applied and the catalog seeded.");
            return Ok;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The database could not be prepared. Nothing that needs it should start.");
            return Failed;
        }
    }
}
