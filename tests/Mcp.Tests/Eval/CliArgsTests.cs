using ExpertToJob.Infrastructure.Embeddings;
using ExpertToJob.RetrievalEval;
using FluentAssertions;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the sweep CLI's argument grammar:
/// <c>[--provider P] [--threshold X | --sweep a:b:c] [--refine] [--output path] [--date d]</c>.
/// </summary>
public class CliArgsTests
{
    /// <summary>With nothing asked for, nothing is decided here: since EXP-64 the provider and its
    /// similarity floor come from <c>Ai:*</c> configuration, and a number compiled into the CLI
    /// would be one provider's plateau applied to another's vectors.</summary>
    [Fact]
    public void Defaults_to_the_configured_provider_and_its_own_floor()
    {
        var args = CliArgs.Parse([]);

        args.Thresholds.Should().BeNull();
        args.Provider.Should().BeNull();
        args.IsSweep.Should().BeFalse();
        args.Refine.Should().BeFalse();
        args.OutputPath.Should().BeNull();
        args.Date.Should().Be("unspecified");
    }

    [Fact]
    public void Parses_a_provider_override()
        => CliArgs.Parse(["--provider", "gemini"]).Provider.Should().Be(EmbeddingsProvider.Gemini);

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
    [InlineData("--provider")]                           // missing value
    [InlineData("--provider", "Azure")]                  // not a provider name
    [InlineData("--provider", "1")]                      // a number is not a name
    [InlineData("--provider", "Gemini,AzureFoundry")]    // two asked for, one silently chosen
    public void Rejects_malformed_argument_lists(params string[] argv)
    {
        var act = () => CliArgs.Parse(argv);

        act.Should().Throw<ArgumentException>();
    }
}
