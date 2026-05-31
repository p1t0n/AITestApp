using EmployeeManager.Application.Availability;
using EmployeeManager.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace EmployeeManager.Application.Tests;

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
}
