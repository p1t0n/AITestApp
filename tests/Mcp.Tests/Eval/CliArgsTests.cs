using CvManager.RetrievalEval;
using FluentAssertions;

namespace CvManager.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the sweep CLI's argument grammar:
/// <c>[--threshold X | --sweep a:b:c] [--refine] [--output path] [--date d]</c>.
/// </summary>
public class CliArgsTests
{
    [Fact]
    public void Defaults_to_a_single_run_at_the_production_threshold()
    {
        var args = CliArgs.Parse([]);

        args.Thresholds.Should().Equal(0.55);
        args.IsSweep.Should().BeFalse();
        args.Refine.Should().BeFalse();
        args.OutputPath.Should().BeNull();
        args.Date.Should().Be("unspecified");
    }

    [Fact]
    public void Parses_a_single_threshold_run()
        => CliArgs.Parse(["--threshold", "0.35"]).Thresholds.Should().Equal(0.35);

    [Fact]
    public void Parses_a_sweep_with_refine_output_and_date()
    {
        var args = CliArgs.Parse(
            ["--sweep", "0.15:0.25:0.05", "--refine", "--output", "report.md", "--date", "2026-07-11"]);

        args.Thresholds.Should().Equal(0.15, 0.20, 0.25);
        args.IsSweep.Should().BeTrue();
        args.Refine.Should().BeTrue();
        args.OutputPath.Should().Be("report.md");
        args.Date.Should().Be("2026-07-11");
    }

    [Theory]
    [InlineData("--threshold")]                          // missing value
    [InlineData("--threshold", "abc")]                   // non-numeric
    [InlineData("--threshold", "0.3", "--sweep", "0.1:0.2:0.05")] // mutually exclusive
    [InlineData("--wat")]                                // unknown flag
    public void Rejects_malformed_argument_lists(params string[] argv)
    {
        var act = () => CliArgs.Parse(argv);

        act.Should().Throw<ArgumentException>();
    }
}
