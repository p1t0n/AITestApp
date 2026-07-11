using EmployeeManager.RetrievalEval;
using FluentAssertions;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the sweep-range grammar (<c>start:end:step</c>) and the refine-window generator.
/// Expected threshold lists are written out by hand — the tests guard against floating-point drift
/// (0.15 + 7×0.05 must land exactly on 0.50) and off-by-one at the inclusive end.
/// </summary>
public class SweepRangeTests
{
    [Fact]
    public void Parses_the_canonical_coarse_sweep_inclusively_and_without_float_drift()
        => SweepRange.Parse("0.15:0.50:0.05").Should().Equal(
            0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50);

    [Fact]
    public void Includes_the_end_only_when_the_step_lands_on_it()
        => SweepRange.Parse("0.10:0.25:0.07").Should().Equal(0.10, 0.17, 0.24);

    [Fact]
    public void A_single_point_range_yields_exactly_that_threshold()
        => SweepRange.Parse("0.30:0.30:0.05").Should().Equal(0.30);

    [Theory]
    [InlineData("0.15:0.50")]           // missing step
    [InlineData("0.15:0.50:0.05:9")]    // too many segments
    [InlineData("a:0.50:0.05")]         // non-numeric
    [InlineData("0.15:0.50:0")]         // zero step
    [InlineData("0.15:0.50:-0.05")]     // negative step
    [InlineData("0.50:0.15:0.05")]      // end before start
    [InlineData("-0.1:0.50:0.05")]      // below the similarity domain
    [InlineData("0.15:1.5:0.05")]       // above the similarity domain
    public void Rejects_malformed_specs_with_a_message_naming_the_expected_grammar(string spec)
    {
        var act = () => SweepRange.Parse(spec);

        act.Should().Throw<ArgumentException>().WithMessage("*start:end:step*");
    }

    [Fact]
    public void Refine_window_spans_the_radius_around_the_center_in_exact_steps()
        => SweepRange.Around(center: 0.30, radius: 0.025, step: 0.005).Should().Equal(
            0.275, 0.280, 0.285, 0.290, 0.295, 0.300, 0.305, 0.310, 0.315, 0.320, 0.325);

    [Fact]
    public void Refine_window_is_clamped_to_the_similarity_domain()
        => SweepRange.Around(center: 0.01, radius: 0.025, step: 0.005).Should().Equal(
            0.000, 0.005, 0.010, 0.015, 0.020, 0.025, 0.030, 0.035);
}
