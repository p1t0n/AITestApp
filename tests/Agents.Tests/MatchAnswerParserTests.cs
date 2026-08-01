using CvManager.Agents.Staffing;
using FluentAssertions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Unit tests for the pure parser that lifts the overall score and band out of the Match agent's
/// markdown answer. The agent's instructions pin the vocabulary ("overall score out of 100" plus a
/// band of Strong / Moderate / Weak / Insufficient evidence) but not the exact formatting, so the
/// parser must ride out the usual markdown variations and return nulls — never throw — when the
/// answer doesn't contain a readable score or band.
/// </summary>
public class MatchAnswerParserTests
{
    [Theory]
    [InlineData("Overall score: 78/100", 78)]
    [InlineData("**Overall Score:** 62 out of 100", 62)]
    [InlineData("The overall score is 45.", 45)]
    [InlineData("Overall score (out of 100): 91", 91)]
    [InlineData("- **Overall score**: 100/100", 100)]
    [InlineData("overall score: 0/100", 0)]
    public void Parses_the_overall_score_from_common_markdown_shapes(string line, int expected)
    {
        var facts = MatchAnswerParser.Parse($"## Fit assessment\n\n{line}\n\nBand: Strong");

        facts.Score.Should().Be(expected);
    }

    [Theory]
    [InlineData("Overall band: Strong", "Strong")]
    [InlineData("Overall score: 60/100 — Moderate", "Moderate")]
    [InlineData("**Band:** weak", "Weak")]
    [InlineData("Overall band: Insufficient evidence", "Insufficient evidence")]
    public void Parses_the_band_and_normalizes_its_casing(string line, string expected)
    {
        var facts = MatchAnswerParser.Parse($"## Fit assessment\n\nOverall score: 60/100\n{line}");

        facts.Band.Should().Be(expected);
    }

    [Fact]
    public void Band_is_only_read_from_band_or_overall_lines_not_from_prose()
    {
        // "Strong" appears in the gap analysis prose but no band/overall line names one.
        var facts = MatchAnswerParser.Parse(
            "Gap analysis: strong Kafka background, Missing: leadership.\n\nNo assessment given.");

        facts.Band.Should().BeNull();
    }

    [Fact]
    public void Score_larger_than_100_is_rejected()
    {
        var facts = MatchAnswerParser.Parse("Overall score: 780/100");

        facts.Score.Should().BeNull();
    }

    [Fact]
    public void Answer_without_a_score_or_band_yields_nulls()
    {
        var facts = MatchAnswerParser.Parse("The employee was not found.");

        facts.Score.Should().BeNull();
        facts.Band.Should().BeNull();
    }

    [Fact]
    public void Empty_answer_yields_nulls()
    {
        var facts = MatchAnswerParser.Parse("");

        facts.Score.Should().BeNull();
        facts.Band.Should().BeNull();
    }
}
