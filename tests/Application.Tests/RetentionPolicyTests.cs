using ExpertToJob.Application.Compliance;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The retention periods themselves (P1T-188), as a pure function of four facts. The sweep, the
/// Art. 15 disclosure and the warning banner all call this, so "when does this record go" has one
/// answer — and it is testable without a database, a clock or a host.
/// </summary>
public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Collected = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_claimed_record_runs_two_years_from_the_persons_last_activity()
    {
        var active = Collected.AddMonths(18);

        var verdict = RetentionPolicy.For("ada@example.dev", isClaimed: true, Collected, active);

        verdict.Clock.Should().Be(RetentionClock.Claimed);
        verdict.AnchoredAt.Should().Be(active, "their last act, not the day we first held them");
        verdict.ExpiresAt.Should().Be(active.AddYears(RetentionPolicy.ClaimedYears));
        verdict.IsExpiredAt(active.AddYears(2).AddDays(-1)).Should().BeFalse(
            "counted in calendar years, so a leap day does not expire somebody early");
        verdict.IsExpiredAt(active.AddYears(2)).Should().BeTrue();
    }

    /// <summary>
    /// Nothing has happened to an unclaimed record since somebody typed it in, so activity is not a
    /// meaningful question about it and the clock runs from collection.
    /// </summary>
    [Fact]
    public void An_unclaimed_record_runs_six_months_from_collection()
    {
        var verdict = RetentionPolicy.For("bench@example.dev", isClaimed: false, Collected, null);

        verdict.Clock.Should().Be(RetentionClock.Unclaimed);
        verdict.AnchoredAt.Should().Be(Collected);
        verdict.ExpiresAt.Should().Be(Collected.AddMonths(RetentionPolicy.UnclaimedMonths));
        verdict.ExpiresAt.Should().BeBefore(
            RetentionPolicy.For("ada@lovelace.dev", isClaimed: true, Collected, null).ExpiresAt!.Value,
            "somebody who never knew we existed is not owed the period somebody who chose to be here is");
    }

    /// <summary>
    /// Even if an unclaimed record somehow carries an activity stamp, collection still governs:
    /// the six months exist because the person cannot be reached, and nothing done <em>to</em> the
    /// record changes that.
    /// </summary>
    [Fact]
    public void Activity_on_an_unclaimed_record_does_not_extend_it()
    {
        var verdict = RetentionPolicy.For(
            "bench@example.dev", isClaimed: false, Collected, Collected.AddMonths(5));

        verdict.ExpiresAt.Should().Be(Collected.AddMonths(RetentionPolicy.UnclaimedMonths));
    }

    [Fact]
    public void A_claimed_record_with_no_activity_yet_falls_back_to_collection()
    {
        var verdict = RetentionPolicy.For("ada@example.dev", isClaimed: true, Collected, null);

        verdict.Clock.Should().Be(RetentionClock.Claimed);
        verdict.AnchoredAt.Should().Be(Collected);
        verdict.ExpiresAt.Should().Be(Collected.AddYears(RetentionPolicy.ClaimedYears));
    }

    /// <summary>
    /// The demo trap. Fabricated rows are not people, so no period applies — and without this the
    /// demo roster evaporates and every developer's local environment empties itself overnight.
    /// </summary>
    [Theory]
    [InlineData("someone@demo.example.com")]
    [InlineData("alice.nguyen@example.com")]
    [InlineData("sample@example.net")]
    [InlineData("someone@my.test")]
    [InlineData("someone@thing.invalid")]
    [InlineData("")]
    public void Fabricated_records_are_never_on_a_clock(string email)
    {
        var verdict = RetentionPolicy.For(email, isClaimed: false, Collected, null);

        verdict.Clock.Should().Be(RetentionClock.NotAPerson);
        verdict.ExpiresAt.Should().BeNull();
        verdict.IsExpiredAt(Collected.AddYears(50)).Should().BeFalse("at any simulated date");
    }

    /// <summary>Keeps the exclusion honest in the other direction: a real address is on a clock, or
    /// the rule would quietly exempt everybody.</summary>
    [Theory]
    [InlineData("ada@lovelace.dev")]
    [InlineData("someone@example.company.com")]
    [InlineData("person@examples.com")]
    public void A_real_address_is_on_a_clock(string email)
    {
        RetentionPolicy.For(email, isClaimed: false, Collected, null)
            .Clock.Should().Be(RetentionClock.Unclaimed);
    }

    [Fact]
    public void The_final_thirty_days_are_a_warning_and_not_an_expiry()
    {
        var verdict = RetentionPolicy.For("ada@lovelace.dev", isClaimed: false, Collected, null);
        var due = verdict.ExpiresAt!.Value;

        verdict.IsInFinalWarningAt(due.AddDays(-31)).Should().BeFalse();
        verdict.IsInFinalWarningAt(due.AddDays(-29)).Should().BeTrue();
        verdict.IsInFinalWarningAt(due.AddDays(1)).Should().BeFalse("past it, this is not a warning");
        verdict.IsExpiredAt(due.AddDays(-29)).Should().BeFalse();
    }

    [Fact]
    public void Every_clock_is_described_to_the_person_in_words()
    {
        foreach (var clock in Enum.GetValues<RetentionClock>())
        {
            RetentionPolicy.DescriptionFor(clock).Should().NotBeNullOrWhiteSpace(
                $"{clock} is disclosed under Art. 15(1)(d) and somebody has to be able to read it");
        }

        RetentionPolicy.DescriptionFor(RetentionClock.Unclaimed).Should().Contain("six months");
        RetentionPolicy.DescriptionFor(RetentionClock.Claimed).Should().Contain("two years");
    }
}
