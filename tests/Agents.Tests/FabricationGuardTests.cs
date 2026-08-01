using CvManager.Agents.Agents;
using FluentAssertions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Pure unit tests for the fabrication guard that vets each model rewrite before it ships:
/// (1) numbers-subset — every numeric token in the rewrite must already appear in the original
/// bullet or its parent experience's text; (2) exemplar-overlap — no verbatim 8-word (by default)
/// n-gram shared with any exemplar shown this run. A violation drops the rewrite (no retries).
/// </summary>
public class FabricationGuardTests
{
    private const string Original = "Cut deploy time 40% by automating the release train.";
    private const string ExperienceContext = "Acme Senior Engineer Jan 2020 – Present Platform work.";

    private static string? Check(
        string rewritten,
        string original = Original,
        string context = ExperienceContext,
        string[]? exemplars = null,
        int nGramWords = 8)
        => FabricationGuard.Check(rewritten, original, context, exemplars ?? [], nGramWords);

    [Fact]
    public void Passes_a_clean_rewrite_whose_numbers_all_come_from_the_original()
    {
        Check("Reduced deployment time by 40% through release automation.").Should().BeNull();
    }

    [Fact]
    public void Drops_a_rewrite_that_fabricates_a_number()
    {
        Check("Cut deploy time 55% by automating the release train.")
            .Should().NotBeNull("55% appears nowhere in the candidate's CV");
    }

    [Fact]
    public void Passes_a_number_that_comes_from_the_experience_context()
    {
        // 2020 is not in the bullet, but the experience period legitimises it.
        Check("Automated the release train from 2020, cutting deploy time 40%.").Should().BeNull();
    }

    [Theory]
    [InlineData("Achieved a 10x speedup in builds.", "Delivered a 10x speedup for the build farm.")]
    [InlineData("Improved uptime to 99.95 percent.", "Raised uptime to 99.95 during peak.")]
    [InlineData("Handled 90k requests per day.", "Scaled the API to 90k daily requests.")]
    [InlineData("Raised coverage by 4.5 points.", "Lifted test coverage 4.5 points.")]
    public void Recognises_multiplier_percent_decimal_and_suffixed_tokens(
        string original, string rewritten)
    {
        Check(rewritten, original).Should().BeNull();
    }

    [Theory]
    [InlineData("Cut deploy time 40%.", "Delivered a 10x faster deploy.")]
    [InlineData("Improved throughput a lot.", "Improved throughput 55%.")]
    [InlineData("Sped up builds.", "Sped up builds 4.5 times.")]
    [InlineData("Scaled the API.", "Scaled the API to 90k requests.")]
    public void Drops_fabricated_multiplier_percent_decimal_and_suffixed_tokens(
        string original, string rewritten)
    {
        Check(rewritten, original).Should().NotBeNull();
    }

    [Fact]
    public void Number_matching_is_case_insensitive()
    {
        Check("Handled 90K requests daily.", "Scaled to 90k requests.").Should().BeNull();
    }

    [Fact]
    public void Drops_a_rewrite_sharing_an_eight_word_run_with_an_exemplar()
    {
        var exemplar = "Reduced settlement lag 55% by rebuilding the reconciliation pipeline end to end.";
        Check(
            "Cut deploy time 40% by rebuilding the reconciliation pipeline end to end for releases.",
            exemplars: [exemplar])
            .Should().NotBeNull("eight consecutive words are copied verbatim from the exemplar");
    }

    [Fact]
    public void Passes_a_rewrite_sharing_only_a_seven_word_run_with_an_exemplar()
    {
        var exemplar = "Reduced settlement lag 40% by rebuilding the reconciliation pipeline end to end.";
        Check(
            "Cut deploy time 40% by rebuilding the reconciliation pipeline end again.",
            exemplars: [exemplar])
            .Should().BeNull("only seven consecutive words overlap, below the 8-word gate");
    }

    [Fact]
    public void Exemplar_overlap_is_case_insensitive_and_ignores_punctuation()
    {
        var exemplar = "rebuilding the release train, cutting deploy time 40% for every team";
        Check(
            "Automated deployments by Rebuilding The Release Train — Cutting Deploy Time 40% For all.",
            exemplars: [exemplar])
            .Should().NotBeNull();
    }

    [Fact]
    public void The_ngram_window_is_configurable()
    {
        var exemplar = "cut deploy time 40% by automating everything";
        // A 6-word verbatim run: passes at the default 8, drops at 6.
        var rewritten = "We cut deploy time 40% by automating a queue.";
        Check(rewritten, exemplars: [exemplar]).Should().BeNull();
        Check(rewritten, exemplars: [exemplar], nGramWords: 6).Should().NotBeNull();
    }

    [Fact]
    public void Passes_when_there_are_no_exemplars()
    {
        Check("Reduced deployment time by 40%.", exemplars: []).Should().BeNull();
    }
}
