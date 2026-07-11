using EmployeeManager.Application.Abstractions;
using EmployeeManager.Infrastructure.Persistence;
using EmployeeManager.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>One golden query's raw result (corpus keys, best first) for diagnostics.</summary>
public sealed record EvalQueryTrace(GoldenQuery Query, IReadOnlyList<string> ReturnedKeys);

/// <summary>Everything one eval run produced: the aggregate metrics plus per-query traces.</summary>
public sealed record EvalRunResult(EvalMetrics Metrics, IReadOnlyList<EvalQueryTrace> Traces);

/// <summary>
/// Runs the retrieval eval through the real production pipeline: seeds the frozen corpus as domain
/// entities, indexes it with <see cref="SearchIndexReconciler"/> (real projection, real chunking),
/// executes every golden query through <see cref="SemanticSearchService"/>, and scores the results
/// with <see cref="RetrievalMetrics"/>. The embedder is injected — pass the real one to measure
/// meaning, a fake one to test plumbing.
/// </summary>
public static class EvalRunner
{
    private const int TopK = 5;

    public static async Task<EvalRunResult> RunAsync(
        Func<AppDbContext> dbFactory,
        IEmbedder embedder,
        IReadOnlyList<EvalEmployee> corpus,
        IReadOnlyList<GoldenQuery> goldenSet,
        CancellationToken ct = default)
    {
        var keysById = await SeedAndIndexAsync(dbFactory, embedder, corpus, ct);

        await using var db = dbFactory();
        var search = new SemanticSearchService(db, embedder,
            Options.Create(new SemanticSearchOptions()), NullLogger<SemanticSearchService>.Instance);

        var traces = new List<EvalQueryTrace>(goldenSet.Count);
        foreach (var query in goldenSet)
        {
            var result = await search.SearchAsync(query.Query, topK: TopK, ct: ct);
            if (result.Error is not null)
            {
                throw new InvalidOperationException(
                    $"Eval query '{query.Query}' failed to run: {result.Error}");
            }

            traces.Add(new EvalQueryTrace(query,
                result.Results.Select(hit => keysById[hit.EmployeeId]).ToList()));
        }

        var outcomes = traces
            .Select(t => new QueryOutcome(
                IsNegative: t.Query.Category == GoldenQueryCategory.Negative,
                Expected: t.Query.Expected.ToHashSet(),
                Returned: t.ReturnedKeys))
            .ToList();

        return new EvalRunResult(RetrievalMetrics.Compute(outcomes), traces);
    }

    /// <summary>Seed the corpus and embed it via the production reconciler; returns id → corpus key.</summary>
    private static async Task<Dictionary<Guid, string>> SeedAndIndexAsync(
        Func<AppDbContext> dbFactory, IEmbedder embedder, IReadOnlyList<EvalEmployee> corpus, CancellationToken ct)
    {
        await using var db = dbFactory();

        var keysById = new Dictionary<Guid, string>();
        foreach (var fixture in corpus)
        {
            var employee = EvalCorpusSeeder.ToEmployee(fixture);
            keysById[employee.Id] = fixture.Key;
            db.Employees.Add(employee);
        }

        await db.SaveChangesAsync(ct);

        await new SearchIndexReconciler(db, embedder,
                Options.Create(new SearchIndexOptions()), NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync(ct);

        return keysById;
    }
}
