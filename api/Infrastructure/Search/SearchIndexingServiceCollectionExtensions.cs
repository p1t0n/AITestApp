using System.Diagnostics.Metrics;
using ExpertToJob.Application.Search;
using ExpertToJob.Infrastructure.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Infrastructure.Search;

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

        // Not bound from a section: the similarity floor below is the only value this object
        // carries, and the sizes beside it are constants (EXP-106). A `SemanticSearch` section in
        // configuration reaches nothing — the one key it used to hold that mattered, MinSimilarity,
        // throws from ResolveProvider rather than binding here.
        var searchOptions = new SemanticSearchOptions();

        // The similarity floor is the one search setting that belongs to the embedding model rather
        // than to the search (EXP-64): Gemini's 0.55 hides 70% of the correct matches on Azure
        // vectors. So it is read from the active provider's block, here, where the single options
        // object every search path shares is built — that is what makes "all three search paths
        // take it from the active provider" true by construction rather than by three call sites
        // remembering to. A leftover global key throws inside ResolveProvider.
        searchOptions.MinSimilarity =
            EmbeddingServiceCollectionExtensions.ResolveProvider(config).Options.MinSimilarity;

        services.AddSingleton(Options.Create(searchOptions));

        // Singleton: a Meter is process-wide and disposing one per reconcile pass would unregister
        // the instrument from every listener. IMeterFactory when the host has one, so the meter is
        // scoped to the host's telemetry rather than to a static.
        services.AddSingleton(sp => new SearchIndexMetrics(sp.GetService<IMeterFactory>()));

        // Scoped: share the request/scope AppDbContext; the worker opens a scope per pass.
        services.AddScoped<SearchIndexReconciler>();
        services.AddScoped<ISemanticSearchService, SemanticSearchService>();
        services.AddScoped<IShortlistSearchService, SemanticSearchService>();
        services.AddScoped<IExemplarSearchService, ExemplarSearchService>();

        return services;
    }
}
