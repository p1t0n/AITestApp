using ExpertToJob.Application.Compliance;

namespace ExpertToJob.Web.Compliance;

/// <summary>How often the retention sweep runs, and whether it runs at all.</summary>
public sealed class RetentionOptions
{
    /// <summary>
    /// Off unless a deployment turns it on. A sweep that deletes people is the one background job
    /// where the safe default is "not running": a developer who pulls this branch, seeds a roster
    /// and leaves the app open overnight should not come back to an empty database. Production
    /// switches it on deliberately, which is also the moment somebody reads what it does.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Hours between passes. A retention period is measured in months and years, so this
    /// is about not hammering the database rather than about precision.</summary>
    public int IntervalHours { get; set; } = 12;
}

/// <summary>
/// Runs the retention sweep on a timer (P1T-188). Shaped after <c>ReconcileWorker</c>: an explicit
/// enabled flag, one scope per pass, and it never lets a failure take the host down — a transient
/// database fault means the next tick tries again, and there is nothing time-critical about a
/// deletion whose deadline was months ago.
///
/// <para>It lives in the Web host because that is where the erasure path is registered — erasure
/// depends on the control-word hasher, which is a Web type. The MCP and Agents hosts cannot resolve
/// it, and giving them a second way to delete people would be exactly the divergence this slice
/// exists to prevent.</para>
/// </summary>
public sealed class RetentionWorker(
    IServiceScopeFactory scopeFactory,
    RetentionOptions options,
    TimeProvider clock,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation(
                "Retention sweep is disabled; no record will expire on this host.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        logger.LogInformation("Retention sweep started (every {Interval}).", interval);

        // The timer takes the injected clock so a test can drive whole months past in milliseconds;
        // the sweep's own boundary arithmetic reads the same clock.
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IRetentionSweep>();
            var result = await sweep.RunOnceAsync(ct);

            if (result.Expired > 0)
            {
                // Worth a line in the log every time: this is the one background job whose normal
                // operation destroys somebody's data, and "it went quiet" must never be the only
                // evidence that it ran.
                logger.LogInformation(
                    "Retention sweep expired {Expired} of {Examined} records.",
                    result.Expired, result.Examined);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            // Never crash the host over a deletion that was already months overdue.
            logger.LogError(ex, "Retention sweep failed; retrying next tick.");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
