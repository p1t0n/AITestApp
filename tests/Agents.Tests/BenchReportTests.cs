using System.Text.Json.Nodes;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Unit tests for the bench report's deterministic pieces (P1T-104): the pure stats composer,
/// the deterministic fallback narrative, and the array-aware expert_list result extraction.
/// </summary>
public class BenchReportTests
{
    private static BenchExpert Emp(int capacity, string title = "Engineer", string? location = "London")
        => new(title, location, capacity);

    private static StaffingProposal Proposal(StaffingProposalStatus status, string jd = "Backend engineer role", params string[] candidates) => new()
    {
        Id = Guid.NewGuid(),
        JobDescription = jd,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        Candidates = candidates
            .Select((name, i) => new StaffingProposalCandidate
            {
                Id = Guid.NewGuid(), ExpertId = Guid.NewGuid(), Name = name, Rank = i + 1,
            })
            .ToList(),
    };

    [Fact]
    public void Composes_capacity_buckets_titles_and_locations()
    {
        var stats = BenchStatsComposer.Compose(
            [
                Emp(100, "Engineer"), Emp(100, "Engineer"), Emp(50, "Designer"),
                Emp(0, "Engineer", "Berlin"), Emp(25, "Engineer", null),
            ],
            proposals: null);

        stats.ActiveExperts.Should().Be(5);
        stats.FullyAvailable.Should().Be(2);
        stats.PartiallyAvailable.Should().Be(2);
        stats.FullyBooked.Should().Be(1);
        stats.AverageCapacityPercent.Should().Be(55.0);
        stats.TopTitles[0].Should().Be(new NameCount("Engineer", 4));
        stats.Locations.Should().Contain(new NameCount("London", 3))
            .And.Contain(new NameCount("Berlin", 1));
        stats.Proposals.Should().BeNull();
    }

    [Fact]
    public void Aggregates_the_proposals_ledger_as_demand_signal()
    {
        var stats = BenchStatsComposer.Compose(
            [Emp(100)],
            [
                Proposal(new StaffingProposalStatus.Approved(), "Kafka platform engineer with leadership", "Ada", "Grace"),
                Proposal(new StaffingProposalStatus.Pending(), "React frontend lead", "Ada"),
                Proposal(new StaffingProposalStatus.Rejected(), "Data engineer", "Ada", "Lin"),
            ]);

        var p = stats.Proposals!;
        p.Total.Should().Be(3);
        p.Pending.Should().Be(1);
        p.Approved.Should().Be(1);
        p.Rejected.Should().Be(1);
        p.RecentJobDescriptions.Should().HaveCount(3);
        p.FrequentCandidates[0].Should().Be(new NameCount("Ada", 3), "repeat shortlisting is the signal");
    }

    [Fact]
    public void Empty_roster_composes_zeroes_and_fallback_still_reads()
    {
        var stats = BenchStatsComposer.Compose(null, null);

        stats.ActiveExperts.Should().Be(0);
        stats.AverageCapacityPercent.Should().Be(0);

        var fallback = BenchStatsComposer.FallbackAnswer(stats);
        fallback.Should().Contain("Active experts: 0");
    }

    [Fact]
    public void Fallback_answer_reads_the_same_average_whatever_culture_the_host_has()
    {
        // `AverageCapacityPercent` is a `double` rounded to one place, and the fallback answer is
        // what a person reads when the model call degrades. Ambient formatting turns 50.5 into
        // `50,5` on a German host — a report that says something different depending on where the
        // service runs (P1T-200).
        var stats = BenchStatsComposer.Compose([Emp(100), Emp(1)], null);

        var fallback = Culture.Under(Culture.Other, () => BenchStatsComposer.FallbackAnswer(stats));

        fallback.Should().Contain("Average available capacity: 50.5%");
        fallback.Should().NotContain("50,5");
    }

    [Fact]
    public void Fallback_answer_carries_the_headline_numbers()
    {
        var stats = BenchStatsComposer.Compose(
            [Emp(100), Emp(0)],
            [Proposal(new StaffingProposalStatus.Pending())]);

        var fallback = BenchStatsComposer.FallbackAnswer(stats);

        fallback.Should().Contain("Active experts: 2");
        fallback.Should().Contain("fully booked: 1");
        fallback.Should().Contain("1 pending");
    }

    [Fact]
    public void Extracts_expert_list_from_plain_arrays_and_mcp_envelopes()
    {
        const string experts =
            """[{"title":"Engineer","location":"London","currentCapacityPercent":100}]""";

        var plain = BenchReportService.ExtractExperts(JsonNode.Parse(experts), 0);
        plain.Should().ContainSingle().Which.CurrentCapacityPercent.Should().Be(100);

        var envelope = JsonNode.Parse(
            $$"""{"content":[{"$type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(experts)}}}]}""");
        var fromEnvelope = BenchReportService.ExtractExperts(envelope, 0);
        fromEnvelope.Should().ContainSingle().Which.Title.Should().Be("Engineer");

        BenchReportService.ExtractExperts(JsonNode.Parse("\"not experts\""), 0).Should().BeNull();
    }

    /// <summary>
    /// EXP-94 moved expert_list's result to {total, items} so a count question needs no row dump.
    /// bench-report calls the same tool directly for its roster stats, and it wants the rows — a
    /// walker that stopped at the envelope would have degraded every report to "roster stats
    /// unavailable (unrecognized expert_list result shape)" with nothing failing.
    /// </summary>
    [Fact]
    public void Extracts_expert_list_rows_from_the_total_and_items_envelope()
    {
        const string result =
            """{"total":1,"items":[{"title":"Engineer","location":"Warsaw, Poland","currentCapacityPercent":80}]}""";

        var plain = BenchReportService.ExtractExperts(JsonNode.Parse(result), 0);
        plain.Should().ContainSingle().Which.Location.Should().Be("Warsaw, Poland");

        var envelope = JsonNode.Parse(
            $$"""{"content":[{"$type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(result)}}}]}""");
        BenchReportService.ExtractExperts(envelope, 0)
            .Should().ContainSingle().Which.CurrentCapacityPercent.Should().Be(80);
    }
}
