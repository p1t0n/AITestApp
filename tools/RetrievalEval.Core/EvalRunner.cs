using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Search;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace ExpertToJob.RetrievalEval;

/// <summary>One golden query's raw result (corpus keys, best first) for diagnostics.</summary>
public sealed record EvalQueryTrace(GoldenQuery Query, IReadOnlyList<string> ReturnedKeys);

/// <summary>Everything one eval run produced: the aggregate metrics plus per-query traces.</summary>
public sealed record EvalRunResult(EvalMetrics Metrics, IReadOnlyList<EvalQueryTrace> Traces);

/// <summary>
/// How query capture handles a soft search error — typically the embedding provider rate-limiting.
/// Unlike the agents' 429 ladders this one retries on a <em>result</em>: the search service answers
/// a soft failure as <see cref="SemanticSearchResult.Error"/> rather than by throwing, so the
/// predicate reads the outcome, not an exception type, and the two cannot share a builder.
/// A budget of one attempt is the empty pipeline — the first soft error is the answer, which is
/// the plumbing/live-test behavior — while <see cref="Default"/> rides out per-minute limits.
/// </summary>
public static class QueryRetry
{
    /// <summary>The shipped ladder: five attempts, waiting 20s, 40s, 60s then 80s.</summary>
    public static ResiliencePipeline<SemanticSearchResult> Default(TimeProvider clock) =>
        Ladder(maxAttempts: 5, step: TimeSpan.FromSeconds(20), clock);

    /// <summary>A linear ladder of <paramref name="maxAttempts"/> tries, the n-th wait being
    /// n × <paramref name="step"/>.</summary>
    public static ResiliencePipeline<SemanticSearchResult> Ladder(
        int maxAttempts, TimeSpan step, TimeProvider clock) =>
        maxAttempts <= 1
            ? ResiliencePipeline<SemanticSearchResult>.Empty
            : new ResiliencePipelineBuilder<SemanticSearchResult> { TimeProvider = clock }
                .AddRetry(new RetryStrategyOptions<SemanticSearchResult>
                {
                    // Polly counts retries; the budget here is attempts, the first one included.
                    MaxRetryAttempts = maxAttempts - 1,
                    BackoffType = DelayBackoffType.Linear,
                    Delay = step,
                    UseJitter = false,
                    ShouldHandle = new PredicateBuilder<SemanticSearchResult>()
                        .HandleResult(result => result.Error is not null),
                })
                .Build();
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

    /// <summary>One full eval at a stated threshold (plumbing- and live-test entry). The threshold
    /// is a parameter rather than a code default since EXP-64: it belongs to the embedding model,
    /// and the two providers' plateaus do not overlap, so a caller has to say which floor it is
    /// measuring against.</summary>
    public static async Task<EvalRunResult> RunAsync(
        Func<AppDbContext> dbFactory,
        IEmbedder embedder,
        IReadOnlyList<EvalExpert> corpus,
        IReadOnlyList<GoldenQuery> goldenSet,
        double threshold,
        CancellationToken ct = default)
    {
        var cached = await CaptureAsync(
            dbFactory, embedder, corpus, goldenSet, threshold, QueryRetry.Default(TimeProvider.System), ct);
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
        IReadOnlyList<EvalExpert> corpus,
        IReadOnlyList<GoldenQuery> goldenSet,
        double floorSimilarity,
        ResiliencePipeline<SemanticSearchResult>? retry = null,
        CancellationToken ct = default)
    {
        retry ??= ResiliencePipeline<SemanticSearchResult>.Empty;
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
                    .Select(hit => new ScoredHit(keysById[hit.ExpertId], hit.Score))
                    .ToList()));
        }

        return cached;
    }

    /// <summary>One query through the search, riding out soft errors on the given ladder. The
    /// ladder hands back the last bad result once the budget is spent; turning that into a hard
    /// failure, named and counted, is this method's share of the work.</summary>
    private static async Task<SemanticSearchResult> SearchWithRetryAsync(
        SemanticSearchService search, GoldenQuery query,
        ResiliencePipeline<SemanticSearchResult> retry, CancellationToken ct)
    {
        var attempts = 0;
        var result = await retry.ExecuteAsync(
            async token =>
            {
                attempts++;
                return await search.SearchAsync(query.Query, topK: TopK, ct: token);
            },
            ct);

        return result.Error is null
            ? result
            : throw new InvalidOperationException(
                $"Eval query '{query.Query}' failed to run after {attempts} attempt(s): {result.Error}");
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
        Func<AppDbContext> dbFactory, IEmbedder embedder, IReadOnlyList<EvalExpert> corpus, CancellationToken ct)
    {
        await using var db = dbFactory();

        var keysById = new Dictionary<Guid, string>();
        foreach (var fixture in corpus)
        {
            var expert = EvalCorpusSeeder.ToExpert(fixture);
            keysById[expert.Id] = fixture.Key;
            db.Experts.Add(expert);
        }

        await db.SaveChangesAsync(ct);

        using var metrics = new SearchIndexMetrics();
        await new SearchIndexReconciler(db, embedder,
                Options.Create(new SearchIndexOptions()), metrics,
                NullLogger<SearchIndexReconciler>.Instance)
            .RunOnceAsync(ct);

        return keysById;
    }
}
