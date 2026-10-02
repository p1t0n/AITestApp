using ExpertToJob.Agents.Handoff;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Status;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The one <see cref="ClosedStatus.Parse{T}"/> that replaced six per-record copies (EXP-98).
///
/// <para><c>Parse</c> is the strict half of the pair: it is for callers that have already
/// established the value is one of the set — a column this code wrote, a request already
/// validated — so "none of them" is a bug worth throwing over rather than a case to degrade. The
/// six copies differed only in how they spelled that error, which is not a difference any caller
/// read: the three real callers are EF converters, and what they need from the message is the
/// offending value and which set it missed. This pins both, for every closed status in the
/// codebase, which is what lets the per-record copies go.</para>
/// </summary>
public class ClosedStatusParseTests
{
    public static TheoryData<string> ProposalStatuses() => new() { "pending", "approved", "rejected" };

    public static TheoryData<string> ScanCandidateStatuses() => new() { "pending", "scored", "failed" };

    public static TheoryData<string> ContestOutcomes() => new() { "upheld", "overturned" };

    public static TheoryData<string> MatchStatuses() => new() { "completed", "failed", "skipped" };

    public static TheoryData<string> StepStatuses() => new() { "started", "completed", "failed" };

    public static TheoryData<string> SliceStatuses() => new() { "completed", "failed", "skipped" };

    [Theory]
    [MemberData(nameof(ProposalStatuses))]
    public void A_known_proposal_status_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<StaffingProposalStatus>(stored).Value.Should().Be(stored);

    [Theory]
    [MemberData(nameof(ScanCandidateStatuses))]
    public void A_known_scan_candidate_status_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<ScoringCandidateStatus>(stored).Value.Should().Be(stored);

    [Theory]
    [MemberData(nameof(ContestOutcomes))]
    public void A_known_contest_outcome_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<ContestOutcome>(stored).Value.Should().Be(stored);

    [Theory]
    [MemberData(nameof(MatchStatuses))]
    public void A_known_match_status_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<StaffingMatchStatus>(stored).Value.Should().Be(stored);

    [Theory]
    [MemberData(nameof(StepStatuses))]
    public void A_known_step_status_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<StaffingStepStatus>(stored).Value.Should().Be(stored);

    [Theory]
    [MemberData(nameof(SliceStatuses))]
    public void A_known_slice_status_parses_back_to_the_case_that_spells_it(string stored) =>
        ClosedStatus.Parse<StageSliceStatus>(stored).Value.Should().Be(stored);

    /// <summary>What the three EF converters need out of the failure: the value that broke the
    /// row, and which set it was measured against. The old per-record wording ("is not a proposal
    /// status", "is not a scan candidate status", …) carried the second in prose; the type name
    /// carries it here, and no caller read either.</summary>
    [Fact]
    public void A_value_outside_the_set_throws_naming_both_the_value_and_the_set()
    {
        var act = () => ClosedStatus.Parse<ScoringCandidateStatus>("abandoned");

        act.Should().Throw<FormatException>()
            .WithMessage("*abandoned*")
            .WithMessage($"*{nameof(ScoringCandidateStatus)}*");
    }

    /// <summary>Empty and whitespace are outside the set like anything else — no case spells
    /// itself that way, so the column holding one is as broken as a typo'd one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pending")]
    public void A_blank_or_miscased_value_is_outside_the_set_too(string stored)
    {
        var act = () => ClosedStatus.Parse<StaffingProposalStatus>(stored);

        act.Should().Throw<FormatException>();
    }
}
