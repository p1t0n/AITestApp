using CvManager.Agents.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CvManager.Agents.Tests;

/// <summary>
/// Unit tests for the interview-kit composition (P1T-102): the markdown kit ships verbatim, the
/// structured questions come from the model's JSON, and every evidence quote is verified against
/// the captured CV — unverifiable quotes drop from the question, the question survives, and
/// corruption degrades to fewer/leaner questions rather than a failed request.
/// </summary>
public class InterviewKitComposerTests
{
    private static readonly InterviewCvPayload Cv = new(
        "Backend engineer focused on payment systems.",
        [
            new TailoringCvExperience(
                Guid.NewGuid(), "Acme", "Senior Engineer", "Jan 2020 – Present",
                "Led the settlement platform team.",
                [new TailoringCvAchievement(Guid.NewGuid(), "Cut deploy time 40% by automating releases.")]),
        ]);

    private static InterviewKitOutcome Outcome(string questionsJson, InterviewCvPayload? cv = null) => new(
        new AgentReply("## Interview kit", 100, 50, 150),
        questionsJson,
        cv ?? Cv);

    private static InterviewKitResponse Compose(InterviewKitOutcome outcome) =>
        InterviewKitComposer.Compose(outcome, NullLogger<InterviewKitComposerTests>.Instance);

    [Fact]
    public void Keeps_questions_whose_evidence_quotes_the_cv_verbatim()
    {
        var response = Compose(Outcome(
            """
            [{"question":"How was the 40% deploy-time cut measured?",
              "probes":"depth of the release automation claim",
              "evidence":"Cut deploy time 40% by automating releases."}]
            """));

        response.Answer.Should().Be("## Interview kit");
        var q = response.Questions.Should().ContainSingle().Subject;
        q.Evidence.Should().Be("Cut deploy time 40% by automating releases.");
        q.Probes.Should().Be("depth of the release automation claim");
    }

    [Fact]
    public void Accepts_evidence_subspans_modulo_case_and_whitespace()
    {
        var response = Compose(Outcome(
            """[{"question":"Q","evidence":"cut   deploy time 40%"}]"""));

        response.Questions.Single().Evidence.Should().NotBeNull();
    }

    [Fact]
    public void Drops_paraphrased_evidence_but_keeps_the_question()
    {
        var response = Compose(Outcome(
            """[{"question":"Tell me about your deployment work.","evidence":"Reduced deployment duration by nearly half."}]"""));

        var q = response.Questions.Should().ContainSingle().Subject;
        q.Question.Should().Be("Tell me about your deployment work.");
        q.Evidence.Should().BeNull("a quote that is not verbatim CV text must not ship as evidence");
    }

    [Fact]
    public void Evidence_from_summaries_passes_and_missing_cv_strips_all_evidence()
    {
        Compose(Outcome("""[{"question":"Q","evidence":"Led the settlement platform team."}]"""))
            .Questions.Single().Evidence.Should().NotBeNull();
        Compose(Outcome("""[{"question":"Q","evidence":"Backend engineer focused on payment systems."}]"""))
            .Questions.Single().Evidence.Should().NotBeNull();

        var noCv = new InterviewKitOutcome(
            new AgentReply("kit", 1, 1, 2),
            """[{"question":"Q","evidence":"Led the settlement platform team."}]""",
            Cv: null);
        Compose(noCv).Questions.Single().Evidence
            .Should().BeNull("with no captured CV nothing is verifiable");
    }

    [Fact]
    public void Blank_questions_drop_and_gap_questions_carry_no_evidence()
    {
        var response = Compose(Outcome(
            """
            [{"question":"  ","evidence":"x"},
             {"question":"Any Kubernetes production experience?","probes":"JD requirement missing from the CV","evidence":""}]
            """));

        var q = response.Questions.Should().ContainSingle().Subject;
        q.Question.Should().StartWith("Any Kubernetes");
        q.Evidence.Should().BeNull();
    }

    [Fact]
    public void Unparseable_or_fenced_model_output_degrades_gracefully()
    {
        Compose(Outcome("total garbage")).Questions.Should().BeEmpty();

        var fenced = Compose(Outcome(
            "Here you go:\n```json\n[{\"question\":\"Q1\"}]\n```"));
        fenced.Questions.Should().ContainSingle().Which.Question.Should().Be("Q1");
    }
}
