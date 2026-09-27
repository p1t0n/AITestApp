using ExpertToJob.Application.Availability;
using ExpertToJob.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class CapacityCalculatorTests
{
    private static AvailabilityEntry Entry(int y, int m, int d, int pct) =>
        new() { EffectiveFrom = new DateOnly(y, m, d), CapacityPercent = pct };

    [Fact]
    public void Returns_zero_when_no_entries()
    {
        CapacityCalculator.CapacityOn(Array.Empty<AvailabilityEntry>(), new DateOnly(2027, 1, 1))
            .Should().Be(0);
    }

    [Fact]
    public void Returns_zero_before_first_entry()
    {
        var entries = new[] { Entry(2027, 4, 1, 50) };
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 3, 31)).Should().Be(0);
    }

    [Fact]
    public void Holds_entry_value_until_next_override()
    {
        // Mirrors the SPEC example: 50% then 75% then 100%.
        var entries = new[]
        {
            Entry(2027, 4, 1, 50),
            Entry(2027, 7, 1, 75),
            Entry(2027, 11, 1, 100),
        };

        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 4, 1)).Should().Be(50);
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 6, 30)).Should().Be(50);
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 7, 1)).Should().Be(75);
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 10, 31)).Should().Be(75);
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 11, 1)).Should().Be(100);
        CapacityCalculator.CapacityOn(entries, new DateOnly(2028, 1, 1)).Should().Be(100);
    }

    [Fact]
    public void Is_order_independent()
    {
        var entries = new[] { Entry(2027, 11, 1, 100), Entry(2027, 4, 1, 50), Entry(2027, 7, 1, 75) };
        CapacityCalculator.CapacityOn(entries, new DateOnly(2027, 8, 1)).Should().Be(75);
    }

    /// <summary>
    /// The step function is written twice (EXP-45): once over a loaded schedule, and once as an
    /// expression EF turns into a correlated subquery so the roster can order and page on
    /// availability in SQL. Two spellings of one rule drift, and the drift would be invisible —
    /// the roster would sort by one number and print another. So they are run against the same
    /// schedules here, and disagreeing fails the build.
    /// </summary>
    [Theory]
    [InlineData(2027, 3, 31)] // before the first entry
    [InlineData(2027, 4, 1)]  // exactly on one
    [InlineData(2027, 6, 30)] // inside a step
    [InlineData(2027, 7, 1)]
    [InlineData(2028, 1, 1)]  // after the last
    public void The_two_spellings_of_the_step_function_agree(int y, int m, int d)
    {
        var on = new DateOnly(y, m, d);
        var expert = new Expert { Id = Guid.NewGuid() };
        foreach (var entry in new[] { Entry(2027, 11, 1, 100), Entry(2027, 4, 1, 50), Entry(2027, 7, 1, 75) })
        {
            expert.AvailabilityEntries.Add(entry);
        }

        CapacityCalculator.CapacityOn(on).Compile()(expert)
            .Should().Be(CapacityCalculator.CapacityOn(expert.AvailabilityEntries, on));
    }

    [Fact]
    public void The_expression_spelling_is_zero_for_an_expert_with_no_schedule_at_all()
    {
        CapacityCalculator.CapacityOn(new DateOnly(2027, 1, 1)).Compile()(new Expert())
            .Should().Be(0, "an empty subquery is a zero, not an absent row");
    }
}
