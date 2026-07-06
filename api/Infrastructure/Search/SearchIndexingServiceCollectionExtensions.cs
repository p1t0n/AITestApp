using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Infrastructure.Search;

/// <summary>
/// Registers the semantic-search reconciler and its options. The hosted worker that drives it on
/// an interval lives in the MCP service (this only provides the reconciler + config it schedules).
/// </summary>
public static class SearchIndexingServiceCollectionExtensions
{
    public static IServiceCollection AddSearchIndexing(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(SearchIndexOptions.Section).Get<SearchIndexOptions>()
                      ?? new SearchIndexOptions();
        services.AddSingleton(Options.Create(options));

        // Scoped: shares the request/scope AppDbContext lifetime; the worker opens a scope per pass.
        services.AddScoped<ISearchIndexReconciler, SearchIndexReconciler>();

        return services;
    }
}
