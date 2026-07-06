using EmployeeManager.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Mcp.Search;

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

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        _logger.LogInformation("Semantic search reconciliation worker started (every {Interval}).", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
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
            catch (Exception ex)
            {
                // Never crash the host: a transient DB/embedding failure just retries next tick.
                _logger.LogError(ex, "Semantic search reconciliation pass failed; retrying next tick.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
