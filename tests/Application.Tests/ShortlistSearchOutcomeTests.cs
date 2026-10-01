using ExpertToJob.Application.Search;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The shortlist retrieval seam is a C# 15 <c>union</c> (EXP-75): a caller gets matches or a
/// fault, never a pair of nullables it has to remember how to read. These pin the two things a
/// union buys that the old record could not — a case is the value (no wrapper to unwrap, and no
/// second field to contradict it), and a <c>switch</c> over both cases is exhaustive without a
/// <c>_</c> arm. The edge mapping back to the published MCP shape is pinned separately, by the
/// literal-JSON guard in <c>RosterShortlistToolsTests</c>.
/// </summary>
public class ShortlistSearchOutcomeTests
{
    private static readonly ShortlistCandidate Ada = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ada Lovelace", "Payments Lead",
        0.75, 1, 2, [new ShortlistRequirementEvidence("kafka", true, "Ran the backbone.", 0.82)]);

    /// <summary>Exhaustive both ways — the arms below are the whole universe of the union, which
    /// is the point: adding a third case would stop this compiling rather than fall through.</summary>
    private static string Describe(ShortlistSearchOutcome outcome) => outcome switch
    {
        ShortlistMatches matches => $"matches:{matches.Results.Count}",
        ShortlistSearchFault fault => $"fault:{fault.Error}",
    };

    [Fact]
    public void A_matches_case_converts_implicitly_and_matches_as_matches()
    {
        ShortlistSearchOutcome outcome = new ShortlistMatches([Ada]);

        Describe(outcome).Should().Be("matches:1");
        (outcome is ShortlistSearchFault).Should().BeFalse();
    }

    [Fact]
    public void A_fault_case_converts_implicitly_and_matches_as_a_fault()
    {
        ShortlistSearchOutcome outcome = new ShortlistSearchFault("The semantic search backend is unavailable.");

        Describe(outcome).Should().Be("fault:The semantic search backend is unavailable.");
        (outcome is ShortlistMatches).Should().BeFalse();
    }

    /// <summary>"No candidates" is a success, not a fault — the distinction the old
    /// <c>(Results, Error)</c> pair left to a convention.</summary>
    [Fact]
    public void An_empty_match_set_is_still_the_matches_case()
    {
        ShortlistSearchOutcome outcome = ShortlistMatches.None;

        Describe(outcome).Should().Be("matches:0");
    }

    [Fact]
    public void The_edge_mapping_flattens_matches_into_the_published_shape()
    {
        var wire = ShortlistSearchResult.From(new ShortlistMatches([Ada]));

        wire.Results.Should().ContainSingle().Which.Name.Should().Be("Ada Lovelace");
        wire.Error.Should().BeNull();
    }

    [Fact]
    public void The_edge_mapping_flattens_a_fault_into_an_empty_list_plus_the_error()
    {
        var wire = ShortlistSearchResult.From(new ShortlistSearchFault("backend down"));

        wire.Results.Should().BeEmpty();
        wire.Error.Should().Be("backend down");
    }
}
