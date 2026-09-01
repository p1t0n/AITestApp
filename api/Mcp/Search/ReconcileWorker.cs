using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Mcp.Search;

/// <summary>
/// Background scheduler for semantic roster search: runs one <see cref="ISearchIndexReconciler"/>
/// pass every <see cref="SearchIndexOptions.IntervalSeconds"/>. A failed pass is logged and retried
/// on the next tick (dirty chunks stay dirty until embedded), so the index self-heals. The
/// reconciler holds all the logic; this is only the loop + a per-pass DI scope.
/// </summary>
public sealed class ReconcileWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchIndexOptions _options;
    private readonly ILogger<ReconcileWorker> _logger;

    public ReconcileWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SearchIndexOptions> options,
        ILogger<ReconcileWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Semantic search reconciliation worker is disabled.");
            return;
        }

        _logger.LogInformation(
            "Semantic search reconciliation worker started (every {Interval}).",
            NextDelay(failure: null, _options));

        while (!stoppingToken.IsCancellationRequested)
        {
            Exception? failure = null;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ISearchIndexReconciler>();
                await reconciler.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (EmbeddingQuotaExceededException ex)
            {
                // The daily cap won't clear on the normal tick; hammering it burns the next
                // day's allowance on guaranteed failures (P1T-98).
                failure = ex;
                _logger.LogWarning(ex,
                    "Embedding quota exhausted; backing off for {Backoff} before the next reconcile pass.",
                    NextDelay(ex, _options));
            }
            catch (Exception ex)
            {
                // Never crash the host: a transient DB/embedding failure just retries next tick.
                failure = ex;
                _logger.LogError(ex, "Semantic search reconciliation pass failed; retrying next tick.");
            }

            try
            {
                await Task.Delay(NextDelay(failure, _options), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>How long to wait before the next pass, given how the last one ended. Pure and
    /// unit-tested directly; quota exhaustion earns the long backoff, everything else stays on
    /// the normal tick.</summary>
    public static TimeSpan NextDelay(Exception? failure, SearchIndexOptions options)
        => failure is EmbeddingQuotaExceededException
            ? TimeSpan.FromSeconds(Math.Max(1, options.QuotaBackoffSeconds))
            : TimeSpan.FromSeconds(Math.Max(1, options.IntervalSeconds));
}
