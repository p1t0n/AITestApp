using EmployeeManager.Application.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Infrastructure.Search;

/// <summary>
/// Registers semantic roster search: the reconciler (indexing) and the query service, plus their
/// options. The hosted worker that drives the reconciler on an interval lives in the MCP service.
/// </summary>
public static class SearchIndexingServiceCollectionExtensions
{
    public static IServiceCollection AddSearchIndexing(this IServiceCollection services, IConfiguration config)
    {
        var indexOptions = config.GetSection(SearchIndexOptions.Section).Get<SearchIndexOptions>()
                           ?? new SearchIndexOptions();
        services.AddSingleton(Options.Create(indexOptions));

        var searchOptions = config.GetSection(SemanticSearchOptions.Section).Get<SemanticSearchOptions>()
                            ?? new SemanticSearchOptions();
        services.AddSingleton(Options.Create(searchOptions));

        // Scoped: share the request/scope AppDbContext; the worker opens a scope per pass.
        services.AddScoped<ISearchIndexReconciler, SearchIndexReconciler>();
        services.AddScoped<ISemanticSearchService, SemanticSearchService>();
        services.AddScoped<IShortlistSearchService, SemanticSearchService>();

        return services;
    }
}
