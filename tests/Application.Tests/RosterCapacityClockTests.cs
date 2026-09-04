using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// "Today" is a dependency, not an ambient fact (P1T-199).
///
/// <para><c>expert_list</c> and <c>expert_get</c> resolve an Expert's <c>currentCapacityPercent</c>
/// from their availability schedule as of a date, and that date used to be <c>DateTime.UtcNow</c>
/// read straight off the machine — while the same service already had a <see cref="TimeProvider"/>
/// injected and used it elsewhere. Two consequences, one of which had already landed:</para>
///
/// <list type="bullet">
/// <item>The Cost Floor over the seeded roster measures a payload whose capacity values change
/// width as the calendar crosses a seeded availability date — <c>80</c> to <c>100</c> is one more
/// character, which is one more token. <c>main</c> went red overnight with no code change.</item>
/// <item>Nothing could ask the roster what it looked like on a given day without also moving the
/// machine clock, which is not a thing a test may do.</item>
/// </list>
/// </summary>
public class RosterCapacityClockTests
{
    /// <summary>A clock pinned to one instant. Small enough to hand-roll rather than take a
    /// package dependency for one property.</summary>
    private sealed class PinnedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"roster-clock-{Guid.NewGuid()}")
            .Options);

    private static ExpertService ServiceOn(AppDbContext db, DateOnly day) =>
        new(db, new SaveExpertValidator(), new UpdateExpertValidator(),
            new UnrestrictedOwnershipScopeProvider(), new AdministrationAudienceProvider(),
            new PinnedClock(day));

    /// <summary>An Expert who is booked solid until the 15th and free from it.</summary>
    private static async Task<Guid> SeedBookedUntilThe15th(AppDbContext db)
    {
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Principal Engineer",
            Email = "ada@example.com",
            Status = Domain.Enums.ExpertStatus.Active,
        };
        expert.AvailabilityEntries.Add(new AvailabilityEntry
        {
            Id = Guid.NewGuid(),
            ExpertId = expert.Id,
            EffectiveFrom = new DateOnly(2026, 8, 1),
            CapacityPercent = 0,
        });
        expert.AvailabilityEntries.Add(new AvailabilityEntry
        {
            Id = Guid.NewGuid(),
            ExpertId = expert.Id,
            EffectiveFrom = new DateOnly(2026, 8, 15),
            CapacityPercent = 100,
        });
        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    [Fact]
    public async Task The_roster_reads_capacity_from_the_injected_clock_rather_than_the_machines()
    {
        await using var db = NewDb();
        await SeedBookedUntilThe15th(db);

        var before = await ServiceOn(db, new DateOnly(2026, 8, 14)).ListAsync();
        var after = await ServiceOn(db, new DateOnly(2026, 8, 15)).ListAsync();

        // The same roster, the same data, two days: the only thing that moved is the clock the
        // service was handed. If `Today` still read `DateTime.UtcNow`, both of these would be
        // whatever today happens to be and this assertion would fail on every day but one.
        before.Single().CurrentCapacityPercent.Should().Be(0);
        after.Single().CurrentCapacityPercent.Should().Be(100);
    }

    [Fact]
    public async Task A_single_expert_reads_the_same_clock_as_the_roster()
    {
        // `expert_get` goes through `ToDetail(Today)` on the same property, and the Cost Floor
        // measures that payload too.
        await using var db = NewDb();
        var id = await SeedBookedUntilThe15th(db);

        var before = await ServiceOn(db, new DateOnly(2026, 8, 14)).GetAsync(id);
        var after = await ServiceOn(db, new DateOnly(2026, 8, 15)).GetAsync(id);

        before.CurrentCapacityPercent.Should().Be(0);
        after.CurrentCapacityPercent.Should().Be(100);
    }
}
