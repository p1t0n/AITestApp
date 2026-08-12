using System.Text.Json.Nodes;
using CvManager.Agents.Agents;
using CvManager.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace CvManager.Agents.Tests;

/// <summary>
/// Unit tests for the bench report's deterministic pieces (P1T-104): the pure stats composer,
/// the deterministic fallback narrative, and the array-aware employee_list result extraction.
/// </summary>
public class BenchReportTests
{
    private static BenchEmployee Emp(int capacity, string title = "Engineer", string? location = "London")
        => new(title, location, capacity);

    private static StaffingProposal Proposal(string status, string jd = "Backend engineer role", params string[] candidates) => new()
    {
        Id = Guid.NewGuid(),
        JobDescription = jd,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        Candidates = candidates
            .Select((name, i) => new StaffingProposalCandidate
            {
                Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Name = name, Rank = i + 1,
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

        stats.ActiveEmployees.Should().Be(5);
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
                Proposal(StaffingProposalStatus.Approved, "Kafka platform engineer with leadership", "Ada", "Grace"),
                Proposal(StaffingProposalStatus.Pending, "React frontend lead", "Ada"),
                Proposal(StaffingProposalStatus.Rejected, "Data engineer", "Ada", "Lin"),
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

        stats.ActiveEmployees.Should().Be(0);
        stats.AverageCapacityPercent.Should().Be(0);

        var fallback = BenchStatsComposer.FallbackAnswer(stats);
        fallback.Should().Contain("Active employees: 0");
    }

    [Fact]
    public void Fallback_answer_carries_the_headline_numbers()
    {
        var stats = BenchStatsComposer.Compose(
            [Emp(100), Emp(0)],
            [Proposal(StaffingProposalStatus.Pending)]);

        var fallback = BenchStatsComposer.FallbackAnswer(stats);

        fallback.Should().Contain("Active employees: 2");
        fallback.Should().Contain("fully booked: 1");
        fallback.Should().Contain("1 pending");
    }

    [Fact]
    public void Extracts_employee_list_from_plain_arrays_and_mcp_envelopes()
    {
        const string employees =
            """[{"title":"Engineer","location":"London","currentCapacityPercent":100}]""";

        var plain = BenchReportService.ExtractEmployees(JsonNode.Parse(employees), 0);
        plain.Should().ContainSingle().Which.CurrentCapacityPercent.Should().Be(100);

        var envelope = JsonNode.Parse(
            $$"""{"content":[{"$type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(employees)}}}]}""");
        var fromEnvelope = BenchReportService.ExtractEmployees(envelope, 0);
        fromEnvelope.Should().ContainSingle().Which.Title.Should().Be("Engineer");

        BenchReportService.ExtractEmployees(JsonNode.Parse("\"not employees\""), 0).Should().BeNull();
    }
}
