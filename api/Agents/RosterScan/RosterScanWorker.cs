using System.Threading.Channels;
using CvManager.Domain.Entities;

namespace CvManager.Agents.RosterScan;

/// <summary>The submit endpoint's hand-off to the background runner: enqueue and return.</summary>
public interface IRosterScanQueue
{
    void Enqueue(Guid jobId);
}

/// <summary>Unbounded in-process channel (the Microsoft queue-service pattern): submissions never
/// block, the worker drains one job at a time — the RPM limiter paces the model calls anyway.</summary>
public sealed class RosterScanQueue : IRosterScanQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid jobId) => _channel.Writer.TryWrite(jobId);
}

/// <summary>
/// The Roster Scan host loop (P1T-124): drains submitted jobs from the channel and sweeps the
/// store on a timer for jobs that became due — paused jobs whose resume time arrived, queued jobs
/// another instance never picked up, and running jobs orphaned by a restart. Jobs run one at a
/// time; each pass gets its own DI scope (the store and usage services are EF-backed and scoped).
/// </summary>
public sealed class RosterScanWorker(
    RosterScanQueue queue,
    IServiceScopeFactory scopeFactory,
    RosterScanOptions options,
    TimeProvider clock,
    ILogger<RosterScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup recovery: whatever the store says is workable gets re-queued — a running row
        // after a restart is an orphan by definition (the runner is in-process).
        await SweepAsync(stoppingToken);

        var timerTask = SweepLoopAsync(stoppingToken);
        await foreach (var jobId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunOneAsync(jobId, stoppingToken);
        }

        await timerTask;
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ResumeSweepSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await SweepAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ScoringJobStore>();
            foreach (var job in await store.FindResumableAsync(ct))
            {
                // Orphaned running / due paused rows re-queue first so the run pass starts from a
                // clean queued state; already-queued rows pass through.
                if (job.State != ScoringJobState.Queued)
                {
                    await store.TryTransitionAsync(job.Id, ScoringJobState.Queued, ct: ct);
                }

                queue.Enqueue(job.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The roster-scan resume sweep failed; the next tick retries.");
        }
    }

    private async Task RunOneAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ScoringJobStore>();
            var job = await store.GetAsync(jobId, ct);
            if (job is null || job.State != ScoringJobState.Queued)
            {
                return; // settled, paused again, or double-enqueued — nothing to do.
            }

            var runner = scope.ServiceProvider.GetRequiredService<RosterScanRunner>();
            await runner.RunAsync(job, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The runner maps its own faults; anything landing here is a worker bug — log and
            // keep the loop alive for the other jobs.
            logger.LogError(ex, "Unexpected worker fault on scoring job {JobId}.", jobId);
        }
    }
}
