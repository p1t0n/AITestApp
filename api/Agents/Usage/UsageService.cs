using CvManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CvManager.Agents.Usage;

/// <summary>Usage for one cap window: how much spent, the cap, and when it resets (UTC).</summary>
public sealed record WindowUsage(string Window, long Used, long Cap, DateTimeOffset ResetAt)
{
    /// <summary>At or over the cap — further calls are blocked until reset.</summary>
    public bool Exceeded => Used >= Cap;
}

/// <summary>Per-agent token total within the monthly window.</summary>
public sealed record AgentBreakdown(string AgentName, long TotalTokens);

/// <summary>A user's current usage across all three windows plus a per-agent breakdown.</summary>
public sealed record UsageSnapshot(
    WindowUsage Daily,
    WindowUsage Weekly,
    WindowUsage Monthly,
    IReadOnlyList<AgentBreakdown> ByAgent);

public interface IUsageService
{
    /// <summary>Current usage snapshot for the user (for the Usage tab / query API).</summary>
    Task<UsageSnapshot> GetSnapshotAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The first window the user has hit, or null if under all caps. Fail-open: returns
    /// null (allow) if usage can't be read, so a DB hiccup never blocks answers.</summary>
    Task<WindowUsage?> FindExceededAsync(Guid userId, CancellationToken ct = default);
}

public sealed class UsageService(
    IAppDbContext db,
    IOptions<UsageOptions> options,
    TimeProvider clock,
    ILogger<UsageService> logger) : IUsageService
{
    private readonly UsageOptions _defaults = options.Value;

    public async Task<UsageSnapshot> GetSnapshotAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var (dayStart, dayReset) = DayWindow(now);
        var (weekStart, weekReset) = WeekWindow(now);
        var (monthStart, monthReset) = MonthWindow(now);

        var caps = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.DailyTokenCap, u.WeeklyTokenCap, u.MonthlyTokenCap })
            .FirstOrDefaultAsync(ct);

        // Pull this month's rows once; the day/week windows are subsets.
        var rows = await db.AgentUsages
            .Where(u => u.UserId == userId && u.Timestamp >= monthStart)
            .Select(u => new { u.Timestamp, u.AgentName, u.TotalTokens })
            .ToListAsync(ct);

        long Sum(DateTimeOffset start) => rows.Where(r => r.Timestamp >= start).Sum(r => r.TotalTokens);

        var byAgent = rows
            .GroupBy(r => r.AgentName)
            .Select(g => new AgentBreakdown(g.Key, g.Sum(r => r.TotalTokens)))
            .OrderByDescending(a => a.TotalTokens)
            .ToList();

        return new UsageSnapshot(
            new WindowUsage("daily", Sum(dayStart), caps?.DailyTokenCap ?? _defaults.DefaultDailyTokens, dayReset),
            new WindowUsage("weekly", Sum(weekStart), caps?.WeeklyTokenCap ?? _defaults.DefaultWeeklyTokens, weekReset),
            new WindowUsage("monthly", Sum(monthStart), caps?.MonthlyTokenCap ?? _defaults.DefaultMonthlyTokens, monthReset),
            byAgent);
    }

    public async Task<WindowUsage?> FindExceededAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var snap = await GetSnapshotAsync(userId, ct);
            return new[] { snap.Daily, snap.Weekly, snap.Monthly }.FirstOrDefault(w => w.Exceeded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cap check failed for user {User}; allowing the call (fail-open).", userId);
            return null;
        }
    }

    private static (DateTimeOffset Start, DateTimeOffset Reset) DayWindow(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddDays(1));
    }

    private static (DateTimeOffset Start, DateTimeOffset Reset) WeekWindow(DateTimeOffset now)
    {
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var sinceMonday = ((int)now.DayOfWeek + 6) % 7; // Mon=0 .. Sun=6
        var start = day.AddDays(-sinceMonday);
        return (start, start.AddDays(7));
    }

    private static (DateTimeOffset Start, DateTimeOffset Reset) MonthWindow(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddMonths(1));
    }
}
