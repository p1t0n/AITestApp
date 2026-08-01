using CvManager.Application.Search;
using FluentAssertions;
using Xunit;

namespace CvManager.Application.Tests;

/// <summary>
/// The exemplar anonymization scrub: source-employee names and their companies must never leave
/// the service inside exemplar text, whatever the casing or position.
/// </summary>
public class ExemplarAnonymizerTests
{
    [Fact]
    public void Replaces_first_and_last_name_wherever_they_appear()
    {
        var scrubbed = ExemplarAnonymizer.Scrub(
            "Ada led the rollout; stakeholders praised Ada and Lovelace alike.",
            "Ada", "Lovelace", []);

        scrubbed.Should().Be("[name] led the rollout; stakeholders praised [name] and [name] alike.");
    }

    [Theory]
    [InlineData("ada shipped it", "[name] shipped it")]
    [InlineData("Praised ADA loudly", "Praised [name] loudly")]
    [InlineData("Handover to Ada", "Handover to [name]")]
    public void Name_matching_is_case_insensitive_at_start_middle_and_end(string input, string expected)
    {
        ExemplarAnonymizer.Scrub(input, "Ada", "Lovelace", []).Should().Be(expected);
    }

    [Fact]
    public void Replaces_single_word_company_names()
    {
        var scrubbed = ExemplarAnonymizer.Scrub(
            "Cut Acme's deploy time by 60% across Acme teams.",
            "Ada", "Lovelace", ["Acme"]);

        scrubbed.Should().Be("Cut [company]'s deploy time by 60% across [company] teams.");
    }

    [Fact]
    public void Replaces_multi_word_company_names_as_a_single_placeholder()
    {
        var scrubbed = ExemplarAnonymizer.Scrub(
            "Scaled the Initech Global Services platform to 2M users.",
            "Ada", "Lovelace", ["Initech Global Services"]);

        scrubbed.Should().Be("Scaled the [company] platform to 2M users.");
    }

    [Fact]
    public void Company_matching_is_case_insensitive()
    {
        var scrubbed = ExemplarAnonymizer.Scrub(
            "At INITECH global services we tripled throughput.",
            "Ada", "Lovelace", ["Initech Global Services"]);

        scrubbed.Should().Be("At [company] we tripled throughput.");
    }

    [Fact]
    public void Longer_company_names_win_over_their_own_prefixes()
    {
        var scrubbed = ExemplarAnonymizer.Scrub(
            "Moved Acme Payments onto the Acme platform.",
            "Ada", "Lovelace", ["Acme", "Acme Payments"]);

        scrubbed.Should().Be("Moved [company] onto the [company] platform.");
    }

    [Fact]
    public void Does_not_scrub_inside_larger_words()
    {
        // "Mark" must not turn "Marked" or "benchmark" into placeholders.
        var scrubbed = ExemplarAnonymizer.Scrub(
            "Marked a 3x benchmark improvement.",
            "Mark", "Otto", []);

        scrubbed.Should().Be("Marked a 3x benchmark improvement.");
    }

    [Fact]
    public void Is_a_no_op_when_neither_names_nor_companies_appear()
    {
        const string text = "Reduced p99 latency by 42% within one quarter.";

        ExemplarAnonymizer.Scrub(text, "Ada", "Lovelace", ["Initech"]).Should().Be(text);
    }

    [Fact]
    public void Ignores_blank_names_and_companies()
    {
        const string text = "Delivered the migration.";

        ExemplarAnonymizer.Scrub(text, "", "  ", ["", "   "]).Should().Be(text);
    }
}
