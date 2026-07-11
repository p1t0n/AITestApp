using EmployeeManager.Agents.Usage;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using EmployeeManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeManager.Agents.Tests;

public class UsageServiceTests
{
    // Thursday 2026-06-25 12:00 UTC. Day starts 06-25, week (Mon) 06-22, month 06-01.
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"cap-{Guid.NewGuid()}")
            .Options);

    private static UsageService Service(AppDbContext db, UsageOptions? opts = null) =>
        new(db, Options.Create(opts ?? new UsageOptions()), new FixedClock(Now), NullLogger<UsageService>.Instance);

    private static async Task AddUsage(AppDbContext db, Guid userId, string agent, long tokens, DateTimeOffset at)
    {
        db.AgentUsages.Add(new AgentUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AgentName = agent,
            Model = "m",
            InputTokens = 0,
            OutputTokens = tokens,
            TotalTokens = tokens,
            Timestamp = at,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Default_caps_are_25k_daily_150k_weekly_500k_monthly()
    {
        var defaults = new UsageOptions();

        defaults.DefaultDailyTokens.Should().Be(25_000);
        defaults.DefaultWeeklyTokens.Should().Be(150_000);
        defaults.DefaultMonthlyTokens.Should().Be(500_000);
    }

    [Fact]
    public async Task No_usage_is_under_all_caps()
    {
        await using var db = NewDb();
        (await Service(db).FindExceededAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Daily_usage_at_cap_is_blocked()
    {
        await using var db = NewDb();
        var user = Guid.NewGuid();
        await AddUsage(db, user, "match", 25_000, Now); // default daily cap is 25000

        var exceeded = await Service(db).FindExceededAsync(user);

        exceeded.Should().NotBeNull();
        exceeded!.Window.Should().Be("daily");
        exceeded.ResetAt.Should().Be(new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Yesterdays_usage_does_not_count_against_today()
    {
        await using var db = NewDb();
        var user = Guid.NewGuid();
        await AddUsage(db, user, "match", 1000, Now.AddDays(-1)); // 06-24: within week+month, not today

        var snap = await Service(db).GetSnapshotAsync(user);

        snap.Daily.Used.Should().Be(0);
        snap.Weekly.Used.Should().Be(1000);
        snap.Monthly.Used.Should().Be(1000);
        (await Service(db).FindExceededAsync(user)).Should().BeNull();
    }

    [Fact]
    public async Task Per_user_cap_override_takes_precedence_over_default()
    {
        await using var db = NewDb();
        var user = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = user,
            Email = "u@x.com",
            ControlWordHash = "h",
            Status = UserStatus.Active,
            DailyTokenCap = 100,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
        await AddUsage(db, user, "match", 150, Now); // over the 100 override, under the 25000 default

        var exceeded = await Service(db).FindExceededAsync(user);

        exceeded!.Window.Should().Be("daily");
        exceeded.Cap.Should().Be(100);
    }

    [Fact]
    public async Task Snapshot_breaks_usage_down_by_agent()
    {
        await using var db = NewDb();
        var user = Guid.NewGuid();
        await AddUsage(db, user, "match", 300, Now);
        await AddUsage(db, user, "roster-qa", 200, Now);

        var snap = await Service(db).GetSnapshotAsync(user);

        snap.ByAgent.Should().HaveCount(2);
        snap.ByAgent[0].AgentName.Should().Be("match"); // ordered by tokens desc
        snap.ByAgent[0].TotalTokens.Should().Be(300);
    }
}
