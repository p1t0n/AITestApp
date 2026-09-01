using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Search;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExpertToJob.RetrievalEval;

/// <summary>One golden query's raw result (corpus keys, best first) for diagnostics.</summary>
public sealed record EvalQueryTrace(GoldenQuery Query, IReadOnlyList<string> ReturnedKeys);

/// <summary>Everything one eval run produced: the aggregate metrics plus per-query traces.</summary>
public sealed record EvalRunResult(EvalMetrics Metrics, IReadOnlyList<EvalQueryTrace> Traces);

/// <summary>
/// How query capture handles a soft search error (typically the embedding provider rate-limiting):
/// up to <paramref name="MaxAttempts"/> tries per query, waiting <paramref name="Delay"/> (keyed by
/// the 1-based count of failures so far) between them. <see cref="None"/> fails on the first error
/// — the plumbing/live-test behavior — while <see cref="Default"/> rides out per-minute limits.
/// </summary>
public sealed record QueryRetryPolicy(int MaxAttempts, Func<int, TimeSpan> Delay)
{
    public static QueryRetryPolicy None { get; } = new(1, _ => TimeSpan.Zero);

    public static QueryRetryPolicy Default { get; } = new(
        MaxAttempts: 5,
        Delay: failures => TimeSpan.FromSeconds(20 * failures));
}

/// <summary>
/// Runs the retrieval eval through the real production pipeline: seeds the frozen corpus as domain
/// entities, indexes it with <see cref="SearchIndexReconciler"/> (real projection, real chunking),
/// executes every golden query through <see cref="SemanticSearchService"/>, and scores the results
/// with <see cref="RetrievalMetrics"/>. The embedder is injected — pass the real one to measure
/// meaning, a fake one to test plumbing.
///
/// <para>The expensive work (embedding the corpus and the queries) happens exactly once, in
/// <see cref="CaptureAsync"/>: it runs the search at a floor threshold and keeps the scored hits,
/// so a threshold sweep is a pure in-memory re-rank per candidate (see
/// <see cref="ThresholdReRanker"/>) rather than a re-embedding per threshold.</para>
/// </summary>
public static class EvalRunner
{
    private const int TopK = 5;

    /// <summary>One full eval at the production default threshold (plumbing- and live-test entry).</summary>
    public static async Task<EvalRunResult> RunAsync(
        Func<AppDbContext> dbFactory,
        IEmbedder embedder,
        IReadOnlyList<EvalEmployee> corpus,
        IReadOnlyList<GoldenQuery> goldenSet,
        CancellationToken ct = default)
    {
        var threshold = new SemanticSearchOptions().MinSimilarity;
        var cached = await CaptureAsync(
            dbFactory, embedder, corpus, goldenSet, threshold, QueryRetryPolicy.Default, ct);
        return ToRunResult(cached, threshold);
    }

    /// <summary>
    /// Seed, index, and run every golden query once at <paramref name="floorSimilarity"/>, keeping
    /// the similarity scores. Everything at or above the floor can then be evaluated without
    /// touching the embedder or the database again.
    /// </summary>
    public static async Task<IReadOnlyList<CachedQueryResult>> CaptureAsync(
        Func<AppDbContext> dbFactory,
        IEmbedder embedder,
        IReadOnlyList<EvalEmployee> corpus,
        IReadOnlyList<GoldenQuery> goldenSet,
        double floorSimilarity,
        QueryRetryPolicy? retry = null,
        CancellationToken ct = default)
    {
        retry ??= QueryRetryPolicy.None;
        var keysById = await SeedAndIndexAsync(dbFactory, embedder, corpus, ct);

        await using var db = dbFactory();
        var search = new SemanticSearchService(db, embedder,
            Options.Create(new SemanticSearchOptions { MinSimilarity = floorSimilarity }),
            NullLogger<SemanticSearchService>.Instance);

        var cached = new List<CachedQueryResult>(goldenSet.Count);
        foreach (var query in goldenSet)
        {
            var result = await SearchWithRetryAsync(search, query, retry, ct);

            cached.Add(new CachedQueryResult(query,
                result.Results
                    .Select(hit => new ScoredHit(keysById[hit.EmployeeId], hit.Score))
                    .ToList()));
        }

        return cached;
    }

    /// <summary>One query through the search, riding out soft errors per the retry policy.</summary>
    private static async Task<SemanticSearchResult> SearchWithRetryAsync(
        SemanticSearchService search, GoldenQuery query, QueryRetryPolicy retry, CancellationToken ct)
    {
        for (var failures = 1; ; failures++)
        {
            var result = await search.SearchAsync(query.Query, topK: TopK, ct: ct);
            if (result.Error is null)
            {
                return result;
            }

            if (failures >= retry.MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"Eval query '{query.Query}' failed to run after {failures} attempt(s): {result.Error}");
            }

            await Task.Delay(retry.Delay(failures), ct);
        }
    }

    /// <summary>Score a capture at one threshold in the classic single-run shape.</summary>
    public static EvalRunResult ToRunResult(IReadOnlyList<CachedQueryResult> cached, double threshold)
    {
        var traces = cached
            .Select(c => new EvalQueryTrace(c.Query, ThresholdReRanker.Apply(c.Hits, threshold)))
            .ToList();

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
