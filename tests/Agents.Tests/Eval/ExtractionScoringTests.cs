using ExpertToJob.Agents.Agents;
using ExpertToJob.ExtractionEval;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests.Eval;

/// <summary>
/// Deterministic tests for the extraction-fidelity scoring (P1T-119): the recall/precision math,
/// and above all the fabrication ladder — invented values on silent slots are gated, honest
/// silence and priority over-claims are not.
/// </summary>
public class ExtractionScoringTests
{
    private static GoldenJd SilentJd() => new(
        "silent", "Engineer wanted.",
        ExpectedConcepts: [["engineer"]],
        MustHaveConcepts: [],
        StatedSeniority: null,
        StatedLocation: null,
        YearsStated: false);

    private static GoldenJd RichJd() => new(
        "rich", "Senior engineer in Berlin: Kafka required, Kubernetes nice to have. 5+ years.",
        ExpectedConcepts: [["kafka"], ["kubernetes"]],
        MustHaveConcepts: [["kafka"]],
        StatedSeniority: JdSeniority.Senior,
        StatedLocation: "Berlin",
        YearsStated: true);

    private static JdRequirement Req(
        string text,
        RequirementPriority priority = RequirementPriority.Unspecified,
        int? minYears = null,
        bool inferred = false) =>
        new(text, RequirementKind.Skill, priority, minYears, text, inferred);

    private static JdExtractionOutcome Outcome(JdRequirements requirements) =>
        new("jd-extraction", new AgentReply("{}", 1, 1, 2), requirements, FaultDetail: null);

    [Fact]
    public void Honest_silence_scores_clean_with_no_fabrications()
    {
        var extraction = new JdRequirements(
            [Req("engineering", inferred: true)], JdSeniority.Unspecified, null, ["JD is vague"]);

        var score = ExtractionScoring.Score(SilentJd(), Outcome(extraction));

        score.Fabrications.Should().BeEmpty();
        score.SeniorityCorrect.Should().BeTrue();
        score.LocationCorrect.Should().BeTrue();
        score.EvidenceVerbatimRate.Should().Be(0, "the single requirement is inferred");
    }

    [Fact]
    public void Invented_values_on_silent_slots_are_each_a_fabrication()
    {
        var extraction = new JdRequirements(
            [Req("blockchain", RequirementPriority.MustHave, minYears: 5)],
            JdSeniority.Senior, "Berlin", []);

        var score = ExtractionScoring.Score(SilentJd(), Outcome(extraction));

        score.Fabrications.Should().HaveCount(4,
            "invented seniority, invented location, invented minYears, and a baseless MustHave");
    }

    [Fact]
    public void Priority_over_claim_on_a_stated_concept_is_a_precision_miss_not_a_fabrication()
    {
        // Kubernetes IS in the JD but only nice-to-have: marking it MustHave dents precision.
        var extraction = new JdRequirements(
            [Req("kafka", RequirementPriority.MustHave), Req("kubernetes", RequirementPriority.MustHave, minYears: 5)],
            JdSeniority.Senior, "Berlin", []);

        var score = ExtractionScoring.Score(RichJd(), Outcome(extraction));

        score.Fabrications.Should().BeEmpty();
        score.MustHavePrecision.Should().Be(0.5);
        score.ConceptRecall.Should().Be(1.0);
    }

    [Fact]
    public void MustHave_with_no_basis_in_the_jd_is_a_fabrication()
    {
        var extraction = new JdRequirements(
            [Req("kafka", RequirementPriority.MustHave), Req("blockchain", RequirementPriority.MustHave)],
            JdSeniority.Senior, "Berlin", []);

        var score = ExtractionScoring.Score(RichJd(), Outcome(extraction));

        score.Fabrications.Should().ContainSingle().Which.Should().Contain("blockchain");
        score.MustHavePrecision.Should().Be(0.5);
    }

    [Fact]
    public void Stated_slot_mismatch_is_an_accuracy_miss_not_a_fabrication()
    {
        var extraction = new JdRequirements(
            [Req("kafka")], JdSeniority.Mid, Location: null, []);

        var score = ExtractionScoring.Score(RichJd(), Outcome(extraction));

        score.SeniorityCorrect.Should().BeFalse();
        score.LocationCorrect.Should().BeFalse();
        score.Fabrications.Should().BeEmpty("missing a stated value is a miss, not an invention");
    }

    [Fact]
    public void Extraction_fault_scores_as_a_fault_not_a_zero()
    {
        var faulted = new JdExtractionOutcome(
            "jd-extraction", new AgentReply("essay", 1, 1, 2), Requirements: null, "did not parse");

        var score = ExtractionScoring.Score(RichJd(), faulted);

        score.Fault.Should().Be("did not parse");
        EvalAggregate.From([score]).FaultCount.Should().Be(1);
    }

    [Fact]
    public void Aggregate_averages_exclude_faulted_jds_but_count_them()
    {
        var good = ExtractionScoring.Score(RichJd(), Outcome(new JdRequirements(
            [Req("kafka", RequirementPriority.MustHave), Req("kubernetes")],
            JdSeniority.Senior, "Berlin or remote", [])));
        var faulted = ExtractionScoring.Score(SilentJd(), new JdExtractionOutcome(
            "jd-extraction", new AgentReply("", 0, 0, 0), null, "boom"));

        var aggregate = EvalAggregate.From([good, faulted]);

        aggregate.ConceptRecall.Should().Be(1.0);
        aggregate.LocationAccuracy.Should().Be(1.0, "'Berlin or remote' contains the stated 'Berlin'");
        aggregate.FaultCount.Should().Be(1);
        aggregate.FabricationCount.Should().Be(0);
    }
}
