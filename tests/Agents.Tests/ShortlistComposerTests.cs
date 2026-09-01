using ExpertToJob.Agents.Agents;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Tests for the endpoint-side composition of the shortlist response: deterministic fields
/// (ids, scores, coverage, evidence) always come from the captured tool result; the model's
/// turn-2 JSON contributes only per-candidate rationales, and any corruption in it degrades
/// to a templated rationale instead of corrupting the candidate list.
/// </summary>
public class ShortlistComposerTests
{
    private static readonly Guid AdaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GraceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ShortlistToolPayload Payload() => new(
    [
        new ShortlistToolCandidate(AdaId, "Ada Lovelace", "Platform Lead", 0.91, 2, 3,
        [
            new ShortlistToolEvidence("event streaming with Kafka", true, "Built Kafka pipelines.", 0.88),
            new ShortlistToolEvidence("Kubernetes operations", true, "Ran K8s clusters.", 0.80),
            new ShortlistToolEvidence("team leadership", false),
        ]),
        new ShortlistToolCandidate(GraceId, "Grace Hopper", "Compiler Engineer", 0.75, 1, 3,
        [
            new ShortlistToolEvidence("event streaming with Kafka", false),
            new ShortlistToolEvidence("Kubernetes operations", false),
            new ShortlistToolEvidence("team leadership", true, "Led the compiler team.", 0.82),
        ]),
    ]);

    private static ShortlistAgentOutcome Outcome(string modelText) => new(
        new AgentReply(modelText, 10, 5, 15),
        ["event streaming with Kafka", "Kubernetes operations", "team leadership"],
        Payload());

    [Fact]
    public void Joins_model_rationales_onto_tool_candidates_by_expert_id()
    {
        var response = ShortlistComposer.Compose(Outcome(
            """
            [{"expertId":"11111111-1111-1111-1111-111111111111","rationale":"Strong Kafka and K8s evidence."},
             {"expertId":"33333333-3333-3333-3333-333333333333","rationale":"Proven team leadership."}]
            """));

        response.Requirements.Should().Equal(
            "event streaming with Kafka", "Kubernetes operations", "team leadership");
        response.Candidates.Should().HaveCount(2);

        var ada = response.Candidates[0];
        ada.ExpertId.Should().Be(AdaId);
        ada.Name.Should().Be("Ada Lovelace");
        ada.Title.Should().Be("Platform Lead");
        ada.Score.Should().BeApproximately(0.91, 0.0001);
        ada.Coverage.Matched.Should().Be(2);
        ada.Coverage.Total.Should().Be(3);
        ada.Requirements.Should().HaveCount(3);
        ada.Requirements[0].Text.Should().Be("event streaming with Kafka");
        ada.Requirements[0].Matched.Should().BeTrue();
        ada.Requirements[0].Snippet.Should().Be("Built Kafka pipelines.");
        ada.Requirements[2].Matched.Should().BeFalse();
        ada.Requirements[2].Snippet.Should().BeNull();
        ada.Rationale.Should().Be("Strong Kafka and K8s evidence.");

        response.Candidates[1].Rationale.Should().Be("Proven team leadership.");
    }

    [Fact]
    public void Ignores_unknown_expert_ids_so_the_candidate_list_is_exactly_the_tools()
    {
        // The model hallucinated an id: it must not appear, and Ada (no rationale from the
        // model) gets the templated fallback.
        var response = ShortlistComposer.Compose(Outcome(
            """
            [{"expertId":"99999999-9999-9999-9999-999999999999","rationale":"Invented person."},
             {"expertId":"33333333-3333-3333-3333-333333333333","rationale":"Proven team leadership."}]
            """));

        response.Candidates.Select(c => c.ExpertId).Should().Equal(AdaId, GraceId);
        response.Candidates[0].Rationale.Should().Be(
            "Matched 2/3 requirements: event streaming with Kafka, Kubernetes operations; missing: team leadership.");
        response.Candidates[1].Rationale.Should().Be("Proven team leadership.");
    }

    [Fact]
    public void Falls_back_to_templated_rationales_when_the_model_output_is_unparseable_prose()
    {
        var response = ShortlistComposer.Compose(Outcome(
            "Here are my thoughts: Ada looks great, Grace maybe."));

        response.Candidates.Should().HaveCount(2);
        response.Candidates[0].Rationale.Should().Be(
            "Matched 2/3 requirements: event streaming with Kafka, Kubernetes operations; missing: team leadership.");
        response.Candidates[1].Rationale.Should().Be(
            "Matched 1/3 requirements: team leadership; missing: event streaming with Kafka, Kubernetes operations.");
    }

    [Fact]
    public void Tolerates_markdown_fences_around_the_models_json()
    {
        var response = ShortlistComposer.Compose(Outcome(
            """
            ```json
            [{"expertId":"11111111-1111-1111-1111-111111111111","rationale":"Strong Kafka evidence."}]
            ```
            """));

        response.Candidates[0].Rationale.Should().Be("Strong Kafka evidence.");
    }

    [Fact]
    public void Blank_or_missing_rationales_fall_back_to_the_template()
    {
        var response = ShortlistComposer.Compose(Outcome(
            """[{"expertId":"11111111-1111-1111-1111-111111111111","rationale":"   "}]"""));

        response.Candidates[0].Rationale.Should().StartWith("Matched 2/3 requirements");
    }
}
