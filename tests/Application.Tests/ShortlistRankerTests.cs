using System.Globalization;
using ExpertToJob.Application.Search;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Unit tests for the pure coverage-first merge: candidates are ranked by how many requirements
/// they match (breadth) before how well they match (similarity).
/// </summary>
public class ShortlistRankerTests
{
    [Fact]
    public void Candidate_matching_4_of_5_outranks_a_single_high_similarity_match()
    {
        var broad = Guid.NewGuid();  // matches 4 of 5 requirements at a modest similarity
        var narrow = Guid.NewGuid(); // matches 1 of 5 at 0.9

        var matches = new[]
        {
            Matches((broad, 0.5), (narrow, 0.9)),
            Matches((broad, 0.5)),
            Matches((broad, 0.5)),
            Matches((broad, 0.5)),
            Matches(),
        };

        var ranked = ShortlistRanker.Rank(Requirements(5), matches, topK: 10);

        ranked.Select(c => c.EmployeeId).Should().ContainInOrder(broad, narrow);
        ranked[0].MatchedCount.Should().Be(4);
        ranked[0].Score.Should().BeGreaterThan(ranked[1].Score);
    }

    [Fact]
    public void Equal_coverage_ties_are_broken_by_mean_similarity()
    {
        var strong = Guid.NewGuid(); // 2 of 3, mean 0.8
        var weak = Guid.NewGuid();   // 2 of 3, mean 0.5

        var matches = new[]
        {
            Matches((strong, 0.9), (weak, 0.5)),
            Matches((strong, 0.7), (weak, 0.5)),
            Matches(),
        };

        var ranked = ShortlistRanker.Rank(Requirements(3), matches, topK: 10);

        ranked.Select(c => c.EmployeeId).Should().ContainInOrder(strong, weak);
        ranked[0].Score.Should().BeGreaterThan(ranked[1].Score);
    }

    [Fact]
    public void TopK_limits_the_number_of_candidates_returned()
    {
        var employees = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var matches = new[] { Matches(employees.Select(e => (e, 0.6)).ToArray()) };

        var ranked = ShortlistRanker.Rank(Requirements(1), matches, topK: 2);

        ranked.Should().HaveCount(2);
    }

    [Fact]
    public void No_requirements_yields_no_candidates()
    {
        var ranked = ShortlistRanker.Rank([], [], topK: 10);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Evidence_lists_every_requirement_and_leaves_missed_ones_without_snippets()
    {
        var employee = Guid.NewGuid();
        var matches = new[]
        {
            Matches((employee, 0.8)),
            Matches(), // missed
        };

        var candidate = ShortlistRanker.Rank(["kafka", "terraform"], matches, topK: 10).Single();

        candidate.MatchedCount.Should().Be(1);
        candidate.Evidence.Should().HaveCount(2);
        candidate.Evidence[0].Should().BeEquivalentTo(
            new ShortlistRequirementEvidence("kafka", true, "snippet 0.8", 0.8));
        candidate.Evidence[1].Should().BeEquivalentTo(
            new ShortlistRequirementEvidence("terraform", false));
    }

    private static IReadOnlyList<string> Requirements(int count) =>
        Enumerable.Range(1, count).Select(i => $"requirement {i}").ToList();

    /// <summary>One requirement's matches: employee -> best chunk (similarity, snippet).</summary>
    private static IReadOnlyDictionary<Guid, ShortlistMatch> Matches(params (Guid Employee, double Similarity)[] entries) =>
        entries.ToDictionary(e => e.Employee, e => new ShortlistMatch(
            e.Similarity, string.Create(CultureInfo.InvariantCulture, $"snippet {e.Similarity}")));
}
