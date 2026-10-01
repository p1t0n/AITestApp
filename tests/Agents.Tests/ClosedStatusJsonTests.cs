using System.Text.Json;
using ExpertToJob.Agents.Handoff;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Domain.Entities;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// How the closed status hierarchies cross the wire (EXP-76): one bare string per case, and a
/// <see cref="JsonException"/> — not something else — for anything outside the set.
///
/// <para>The exception type is the load-bearing part. <c>StaffingHandoffDocument.TryDeserialize</c>
/// catches <see cref="JsonException"/> and only that, because its promise is that a stored document
/// nobody can parse degrades the drill-in to <c>package: null</c> rather than throwing over a
/// decision record. A converter that threw <c>FormatException</c> instead would turn a column with
/// one odd status into a 500 on a page that used to render. So the strictness this adds — a status
/// outside the set is no longer silently carried through — lands on the path that already handles
/// it, not on a new one.</para>
/// </summary>
public class ClosedStatusJsonTests
{
    public static TheoryData<StaffingMatchStatus, string> MatchStatuses() => new()
    {
        { new StaffingMatchStatus.Completed(), "completed" },
        { new StaffingMatchStatus.Failed(), "failed" },
        { new StaffingMatchStatus.Skipped(), "skipped" },
    };

    public static TheoryData<StaffingStepStatus, string> StepStatuses() => new()
    {
        { new StaffingStepStatus.Started(), "started" },
        { new StaffingStepStatus.Completed(), "completed" },
        { new StaffingStepStatus.Failed(), "failed" },
    };

    public static TheoryData<StageSliceStatus, string> SliceStatuses() => new()
    {
        { new StageSliceStatus.Completed(), "completed" },
        { new StageSliceStatus.Failed(), "failed" },
        { new StageSliceStatus.Skipped(), "skipped" },
    };

    public static TheoryData<ScoringCandidateStatus, string> CandidateStatuses() => new()
    {
        { new ScoringCandidateStatus.Pending(), "pending" },
        { new ScoringCandidateStatus.Scored(), "scored" },
        { new ScoringCandidateStatus.Failed(), "failed" },
    };

    public static TheoryData<StaffingProposalStatus, string> ProposalStatuses() => new()
    {
        { new StaffingProposalStatus.Pending(), "pending" },
        { new StaffingProposalStatus.Approved(), "approved" },
        { new StaffingProposalStatus.Rejected(), "rejected" },
    };

    public static TheoryData<ContestOutcome, string> ContestOutcomes() => new()
    {
        { new ContestOutcome.Upheld(), "upheld" },
        { new ContestOutcome.Overturned(), "overturned" },
    };

    [Theory]
    [MemberData(nameof(MatchStatuses))]
    public void A_match_status_is_its_string(StaffingMatchStatus status, string wire) => RoundTrips(status, wire);

    [Theory]
    [MemberData(nameof(StepStatuses))]
    public void A_step_status_is_its_string(StaffingStepStatus status, string wire) => RoundTrips(status, wire);

    [Theory]
    [MemberData(nameof(SliceStatuses))]
    public void A_slice_status_is_its_string(StageSliceStatus status, string wire) => RoundTrips(status, wire);

    [Theory]
    [MemberData(nameof(CandidateStatuses))]
    public void A_scan_candidate_status_is_its_string(ScoringCandidateStatus status, string wire) =>
        RoundTrips(status, wire);

    [Theory]
    [MemberData(nameof(ProposalStatuses))]
    public void A_proposal_status_is_its_string(StaffingProposalStatus status, string wire) =>
        RoundTrips(status, wire);

    [Theory]
    [MemberData(nameof(ContestOutcomes))]
    public void A_contest_outcome_is_its_string(ContestOutcome outcome, string wire) => RoundTrips(outcome, wire);

    [Fact]
    public void A_string_outside_the_set_is_a_JsonException()
    {
        var read = () => JsonSerializer.Deserialize<StageSliceStatus>("\"abandoned\"");

        read.Should().Throw<JsonException>().WithMessage("*abandoned*");
    }

    [Fact]
    public void A_status_that_is_not_even_a_string_is_a_JsonException()
    {
        var read = () => JsonSerializer.Deserialize<StaffingMatchStatus>("{\"status\":\"completed\"}");

        read.Should().Throw<JsonException>();
    }

    /// <summary>The degrade the exception type exists for: a stored document carrying a status this
    /// code does not know comes back null, so the drill-in serves its metadata with
    /// <c>package: null</c> — the same answer a pre-package row gets.</summary>
    [Fact]
    public void A_stored_document_with_an_unknown_status_degrades_to_null_rather_than_throwing()
    {
        var document = StaffingHandoffDocument.From(
            new HandoffPackage(
                new Dictionary<string, string?>(),
                new RunProvenance(null, [], DateTimeOffset.UnixEpoch),
                [],
                []),
            new StaffingReport([], [], null, false, []));
        var stored = document.Serialize().Replace(
            "\"slices\":[]", """"slices":[{"stage":"match","scopes":[],"inputTokens":0,"outputTokens":0,"status":"abandoned"}]"""");

        StaffingHandoffDocument.TryDeserialize(stored).Should().BeNull();
    }

    private static void RoundTrips<T>(T status, string wire)
    {
        JsonSerializer.Serialize(status).Should().Be($"\"{wire}\"");
        JsonSerializer.Deserialize<T>($"\"{wire}\"").Should().Be(status);
    }
}
