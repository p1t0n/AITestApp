using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Swaps the host's Postgres context for an in-memory one. Every faked Agents host needs this since
/// P1T-181: the session token is checked against the account it names on every request, so a host
/// with no reachable database cannot authenticate anyone — even a test that never reads a row.
/// </summary>
internal static class InMemoryDatabaseExtensions
{
    /// <summary>
    /// Registers a private in-memory database. The name is captured here rather than built inside
    /// the options callback, because that callback runs per <see cref="AppDbContext"/> instance — a
    /// name generated inside it hands every scope its own empty database.
    /// </summary>
    public static IServiceCollection AddInMemoryAppDb(this IServiceCollection services, string prefix)
    {
        var dbName = $"{prefix}-{Guid.NewGuid()}";
        services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
        services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
            .IDbContextOptionsConfiguration<AppDbContext>));
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services;
    }
}
