using EmployeeManager.Agents.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Tests for the endpoint-side composition of the hybrid tailoring response: the answer is turn
/// 1's markdown verbatim; every deterministic rewrite field (experienceId, achievementId, the
/// original bullet) comes from the captured cv_get result, and the model's turn-2 JSON contributes
/// only the rewritten strings. Corrupt model output degrades to fewer (or no) rewrites — never to
/// fabricated ids/originals, and never to a failed request.
/// </summary>
public class TailoringComposerTests
{
    private static readonly Guid Achievement1 = Guid.Parse("aaaaaaa1-1111-1111-1111-111111111111");
    private static readonly Guid Achievement2 = Guid.Parse("aaaaaaa2-2222-2222-2222-222222222222");
    private static readonly Guid Experience1 = Guid.Parse("eeeeeee1-1111-1111-1111-111111111111");
    private static readonly Guid Experience2 = Guid.Parse("eeeeeee2-2222-2222-2222-222222222222");

    private const string Answer = "Tailored summary: Ada is a strong platform fit.";

    private static TailoringCvPayload Cv() => new(
    [
        new TailoringCvExperience(Experience1, "Acme", "Senior Engineer", "Jan 2020 – Present", "Platform work.",
        [
            new TailoringCvAchievement(Achievement1, "Cut deploy time 40%."),
        ]),
        new TailoringCvExperience(Experience2, "Initech", "Engineer", "Mar 2016 – Dec 2019", null,
        [
            new TailoringCvAchievement(Achievement2, "Led migration to Kubernetes."),
        ]),
    ]);

    private static TailoringExemplarPayload Exemplars() => new(
    [
        new TailoringBulletExemplars(Achievement1,
            [new TailoringExemplar("Reduced [company] settlement lag 55% by rebuilding the reconciliation pipeline end to end.", 0.82)]),
    ]);

    private static TailoringAgentOutcome Outcome(
        string rewritesText,
        TailoringCvPayload? cv = null,
        TailoringExemplarPayload? exemplars = null,
        IReadOnlyList<Guid>? selected = null) => new(
        new AgentReply(Answer, 10, 5, 15),
        rewritesText,
        selected ?? [Achievement1, Achievement2],
        exemplars,
        cv);

    private static TailoringResponse Compose(TailoringAgentOutcome outcome, ILogger? logger = null)
        => TailoringComposer.Compose(outcome, logger ?? NullLogger.Instance);

    [Fact]
    public void Joins_model_rewrites_onto_cv_originals_by_achievement_id()
    {
        var response = Compose(Outcome(
            """
            [{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 40% through release automation."},
             {"achievementId":"aaaaaaa2-2222-2222-2222-222222222222","rewritten":"Led the Kubernetes migration for the platform."}]
            """,
            cv: Cv(),
            exemplars: Exemplars()));

        response.Answer.Should().Be(Answer, "the answer is turn 1's markdown, untouched");
        response.Rewrites.Should().HaveCount(2);

        var first = response.Rewrites[0];
        first.ExperienceId.Should().Be(Experience1);
        first.AchievementId.Should().Be(Achievement1);
        first.Original.Should().Be("Cut deploy time 40%.", "the original comes from CV data, not model text");
        first.Rewritten.Should().Be("Cut deploy time 40% through release automation.");

        var second = response.Rewrites[1];
        second.ExperienceId.Should().Be(Experience2);
        second.AchievementId.Should().Be(Achievement2);
        second.Original.Should().Be("Led migration to Kubernetes.");
    }

    [Fact]
    public void Drops_entries_with_unknown_or_corrupted_achievement_ids()
    {
        var response = Compose(Outcome(
            """
            [{"achievementId":"99999999-9999-9999-9999-999999999999","rewritten":"Cut deploy time 40% instantly."},
             {"achievementId":"not-a-guid","rewritten":"Cut deploy time 40% magically."},
             {"achievementId":"aaaaaaa2-2222-2222-2222-222222222222","rewritten":"Led the Kubernetes migration."}]
            """,
            cv: Cv()));

        response.Rewrites.Should().ContainSingle().Which.AchievementId.Should().Be(Achievement2);
    }

    [Fact]
    public void Drops_entries_for_bullets_the_model_never_selected_for_exemplar_search()
    {
        // Achievement2 exists in the CV but was not among the ids sent to style_exemplar_search.
        var response = Compose(Outcome(
            """[{"achievementId":"aaaaaaa2-2222-2222-2222-222222222222","rewritten":"Led the Kubernetes migration."}]""",
            cv: Cv(),
            selected: [Achievement1]));

        response.Rewrites.Should().BeEmpty();
    }

    [Fact]
    public void Rewrites_survive_when_the_exemplar_call_never_happened()
    {
        // Degrade path: no exemplar tool call means no selected-id gate and no overlap rule —
        // CV membership alone decides.
        var response = Compose(Outcome(
            """[{"achievementId":"aaaaaaa2-2222-2222-2222-222222222222","rewritten":"Led the Kubernetes migration."}]""",
            cv: Cv(),
            selected: []));

        response.Rewrites.Should().ContainSingle().Which.AchievementId.Should().Be(Achievement2);
    }

    [Fact]
    public void Tolerates_markdown_fences_around_the_models_json()
    {
        var response = Compose(Outcome(
            """
            ```json
            [{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 40% through automation."}]
            ```
            """,
            cv: Cv()));

        response.Rewrites.Should().ContainSingle()
            .Which.Rewritten.Should().Be("Cut deploy time 40% through automation.");
    }

    [Fact]
    public void Drops_blank_rewrites_and_degrades_unparseable_prose_to_answer_only()
    {
        Compose(Outcome(
            """[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"   "}]""",
            cv: Cv())).Rewrites.Should().BeEmpty();

        var prose = Compose(Outcome("I would rewrite these bullets as follows: ...", cv: Cv()));
        prose.Answer.Should().Be(Answer);
        prose.Rewrites.Should().BeEmpty();
    }

    [Fact]
    public void Returns_answer_only_when_no_cv_was_captured()
    {
        var response = Compose(Outcome(
            """[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 40%!"}]""",
            cv: null));

        response.Answer.Should().Be(Answer);
        response.Rewrites.Should().BeEmpty();
    }

    [Fact]
    public void Drops_and_logs_a_rewrite_that_fabricates_a_number()
    {
        var logger = new Fakes.CollectingLogger();
        var response = Compose(Outcome(
            """
            [{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 55% through automation."},
             {"achievementId":"aaaaaaa2-2222-2222-2222-222222222222","rewritten":"Led the Kubernetes migration."}]
            """,
            cv: Cv(),
            exemplars: Exemplars()), logger);

        response.Rewrites.Should().ContainSingle("the fabricated 55% must drop only that rewrite")
            .Which.AchievementId.Should().Be(Achievement2);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("55%"));
    }

    [Fact]
    public void Drops_and_logs_a_rewrite_copied_verbatim_from_an_exemplar()
    {
        var logger = new Fakes.CollectingLogger();
        var response = Compose(Outcome(
            """[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 40% by rebuilding the reconciliation pipeline end to end."}]""",
            cv: Cv(),
            exemplars: Exemplars()), logger);

        response.Rewrites.Should().BeEmpty();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("exemplar"));
    }

    [Fact]
    public void A_number_from_the_experience_context_passes_the_guard()
    {
        // 2020 comes from the experience period, not the bullet — legitimate.
        var response = Compose(Outcome(
            """[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Since 2020, cut deploy time 40%."}]""",
            cv: Cv()));

        response.Rewrites.Should().ContainSingle();
    }
}
