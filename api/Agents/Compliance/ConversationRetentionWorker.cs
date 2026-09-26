using ExpertToJob.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Agents.Compliance;

/// <summary>Whether the conversation sweep runs, and how often.</summary>
public sealed class ConversationRetentionOptions
{
    public const string Section = "ConversationRetention";

    /// <summary>
    /// <b>On unless a deployment turns it off</b> — the opposite default to the Expert
    /// <c>Retention:Enabled</c> sweep, and deliberately so (ADR §6). That one erases *people*, so
    /// "not running" is its safe direction. This one deletes transcripts of questions somebody
    /// typed, and the promise the DPIA rests on is that they are gone six months after the last
    /// one. A promise that only holds where an operator remembered a flag is not a promise.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between passes. The period is measured in months, so this is about not
    /// scanning the table needlessly rather than about precision.</summary>
    public int IntervalHours { get; set; } = 24;
}

/// <summary>
/// Deletes every Roster Q&amp;A conversation whose last activity is more than six calendar months
/// old (EXP-35, <c>manuals/adr-roster-qa-conversation-history.md</c> §6).
///
/// <para>The cutoff is <c>AddMonths(-6)</c> and not a span of days, for the same reason the Expert
/// retention periods are calendar years: six months crosses a different number of days depending on
/// where it starts, and a promise that quietly expires somebody's transcript early is a promise not
/// kept. The turns and their touched-Expert rows are taken by the database's own cascade, which is
/// why this deletes conversations and names nothing beneath them.</para>
///
/// <para>It touches the conversation tables and nothing else. No Expert row, no
/// <c>ProcessingRecord</c>, and above all no Retention Clock: agent activity never moves that clock
/// (<c>manuals/retention.md</c> §2), and an agent's transcript expiring is not an exception.</para>
/// </summary>
public sealed class ConversationRetentionSweep(IAppDbContext db, TimeProvider clock)
{
    /// <summary>The cutoff a sweep at <paramref name="now"/> uses. Public so the history API can
    /// tell an owner when a conversation disappears without restating the rule.</summary>
    public static DateTimeOffset CutoffFor(DateTimeOffset now) => now.AddMonths(-6);

    /// <summary>Deletes the stale conversations and returns how many went.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = CutoffFor(clock.GetUtcNow());

        // Loaded and removed rather than ExecuteDelete: the delete has to reach the turns and the
        // touched rows, and it is the configured ON DELETE CASCADE that takes them. A sweep of a
        // six-month-stale set is small by construction, and it runs once a day.
        var stale = await db.RosterQaConversations
            .Where(c => c.LastActiveAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            return 0;
        }

        db.RosterQaConversations.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}

/// <summary>
/// Runs <see cref="ConversationRetentionSweep"/> daily in the Agents host, which is the host that
/// owns this data (ADR §6). Shaped after the Web host's <c>RetentionWorker</c>: an explicit enabled
/// flag, one DI scope per pass, and a failure never takes the host down — a transient database
/// fault means the next tick tries again, and nothing here is time-critical to the hour.
/// </summary>
public sealed class ConversationRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ConversationRetentionOptions options,
    TimeProvider clock,
    ILogger<ConversationRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogWarning(
                "Roster Q&A conversation retention is disabled; no conversation will expire on this host.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        logger.LogInformation("Roster Q&A conversation retention started (every {Interval}).", interval);

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
            var sweep = scope.ServiceProvider.GetRequiredService<ConversationRetentionSweep>();
            var deleted = await sweep.RunOnceAsync(ct);

            if (deleted > 0)
            {
                // Logged every time it does anything: this job's normal operation destroys somebody's
                // conversation, and silence must never be the only evidence that it ran.
                logger.LogInformation(
                    "Roster Q&A conversation retention deleted {Deleted} conversation(s) last active more "
                    + "than six months ago.", deleted);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roster Q&A conversation retention failed; retrying next tick.");
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
