using CvManager.Application.Search;
using FluentAssertions;
using Xunit;

namespace CvManager.Application.Tests;

/// <summary>
/// The quality gate for exemplar bullets: only quantified achievements (a number, a percent, or an
/// Nx multiplier) within a sane length band qualify as style exemplars.
/// </summary>
public class ExemplarQualityFilterTests
{
    [Theory]
    [InlineData("Reduced p99 latency by 250 milliseconds for checkout")]
    [InlineData("Cut infrastructure spend by 42% over two quarters")]
    [InlineData("Tripled throughput to 3x baseline during peak season")]
    public void Quantified_bullets_pass(string text)
    {
        ExemplarQualityFilter.Passes(text, minChars: 40, maxChars: 300).Should().BeTrue();
    }

    [Theory]
    [InlineData("Substantially improved reliability of the core platform")]
    [InlineData("Led the team through a very successful cloud migration")]
    public void Unquantified_bullets_fail(string text)
    {
        ExemplarQualityFilter.Passes(text, minChars: 40, maxChars: 300).Should().BeFalse();
    }

    [Fact]
    public void Length_band_is_inclusive_at_both_edges()
    {
        var atMin = "Cut costs 15%".PadRight(40, '.');
        var atMax = "Cut costs 15%".PadRight(300, '.');

        ExemplarQualityFilter.Passes(atMin, 40, 300).Should().BeTrue();
        ExemplarQualityFilter.Passes(atMax, 40, 300).Should().BeTrue();
    }

    [Fact]
    public void Too_short_or_too_long_bullets_fail_even_when_quantified()
    {
        var tooShort = "Cut costs 15%".PadRight(39, '.');
        var tooLong = "Cut costs 15%".PadRight(301, '.');

        ExemplarQualityFilter.Passes(tooShort, 40, 300).Should().BeFalse();
        ExemplarQualityFilter.Passes(tooLong, 40, 300).Should().BeFalse();
    }
}
