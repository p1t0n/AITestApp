using EmployeeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Tools.SeedDemoRoster;

/// <summary>
/// Repo tool (P1T-51): seeds the committed 500-employee demo roster into a database, or wipes it
/// back out. Idempotent — re-running skips employees whose email already exists. Embeddings are
/// not produced here; the MCP service's reconcile worker indexes new employees on its own.
///
/// Usage: dotnet run -- [--count N] [--wipe] [--connection "..."]
///   --count N       seed only the first N dataset employees (default: all 500)
///   --wipe          delete every employee whose email ends @demo.example.com, then exit
///                   (combine with --count to wipe and reseed in one run)
///   --connection    Postgres connection string; falls back to ConnectionStrings__Default,
///                   then the local dev default.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        int? count = null;
        var wipe = false;
        string? connection = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--count": count = int.Parse(args[++i]); break;
                case "--wipe": wipe = true; break;
                case "--connection": connection = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: [--count N] [--wipe] [--connection \"...\"]");
                    return 1;
            }
        }

        // Same fallback chain as DesignTimeDbContextFactory.
        connection ??= Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=employeemanager;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector())
            .Options;
        await using var db = new AppDbContext(options);

        if (wipe)
        {
            var wiped = await DemoRosterSeeder.WipeAsync(db);
            Console.WriteLine($"Wiped {wiped} demo employees (emails ending {DemoRosterSeeder.DemoEmailSuffix}).");
            if (count is null)
                return 0; // wipe-only run: no reseed unless --count asked for one
        }

        var dataset = DemoRosterSeeder.LoadCommittedDataset();
        var result = await DemoRosterSeeder.SeedAsync(db, dataset, count);
        Console.WriteLine(
            $"Seeded {result.Seeded} demo employees, skipped {result.Skipped} already present " +
            $"(requested {count?.ToString() ?? "all"} of {dataset.Employees.Count}).");
        return 0;
    }
}
