using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// JdRequirementExtractor semantics over a scripted chat client (P1T-116): the native-schema
/// request shape, the honest round-trip (unspecified/null survive untouched), the evidence
/// verification ladder (verbatim passes, paraphrase/missing marks inferred, nothing is stripped),
/// and the unparseable-reply degrade.
/// </summary>
public class JdRequirementExtractorTests
{
    private const string Jd =
        "We need a senior backend engineer, 5+ years with event streaming platforms.\n" +
        "Kubernetes experience is a plus. Based in Amsterdam.";

    [Fact]
    public async Task Requests_native_json_schema_and_parses_the_structured_reply()
    {
        var chat = new FakeChatClient(() => Reply(
            """
            {"requirements":[{"text":"event streaming","kind":"Skill","priority":"MustHave",
              "minYears":5,"evidenceSpan":"5+ years with event streaming platforms","inferred":false}],
             "seniority":"Senior","location":"Amsterdam","ambiguities":[]}
            """,
            input: 100, output: 40, model: "gemini-3.5-flash-lite"));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        outcome.FaultDetail.Should().BeNull();
        outcome.AgentName.Should().Be("jd-extraction");
        outcome.Requirements!.Requirements.Should().ContainSingle()
            .Which.Text.Should().Be("event streaming");
        outcome.Requirements.Seniority.Should().Be(JdSeniority.Senior);

        // The method the P1T-115 probes locked: a schema-bound response format, no prompt-side
        // schema injection needed.
        chat.ReceivedOptions.Should().ContainSingle();
        chat.ReceivedOptions[0]!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>()
            .Which.Schema.Should().NotBeNull();

        // Tokens were spent; the caller meters this reply.
        outcome.Reply.InputTokens.Should().Be(100);
        outcome.Reply.OutputTokens.Should().Be(40);
        outcome.Reply.ModelId.Should().Be("gemini-3.5-flash-lite");
    }

    [Fact]
    public async Task Sparse_jd_honesty_round_trips_untouched()
    {
        var chat = new FakeChatClient(() => Reply(
            """
            {"requirements":[{"text":"engineering","kind":"Other","priority":"Unspecified",
              "minYears":null,"evidenceSpan":null,"inferred":true}],
             "seniority":"Unspecified","location":null,
             "ambiguities":["The JD does not state seniority or required experience."]}
            """));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync("Engineer wanted.");

        var result = outcome.Requirements!;
        result.Seniority.Should().Be(JdSeniority.Unspecified);
        result.Location.Should().BeNull();
        result.Ambiguities.Should().ContainSingle();
        var requirement = result.Requirements.Single();
        requirement.Priority.Should().Be(RequirementPriority.Unspecified);
        requirement.MinYears.Should().BeNull();
        requirement.Inferred.Should().BeTrue();
    }

    [Fact]
    public async Task Verbatim_evidence_span_stays_verified()
    {
        var chat = new FakeChatClient(() => Reply(RequirementWithSpan(
            // Different case + collapsed whitespace: still verbatim under the interview-kit rule.
            "KUBERNETES   experience is a plus")));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        outcome.Requirements!.Requirements.Single().Inferred.Should().BeFalse();
    }

    [Fact]
    public async Task Paraphrased_evidence_span_marks_the_requirement_inferred_but_keeps_it()
    {
        var chat = new FakeChatClient(() => Reply(RequirementWithSpan(
            "knows their way around container orchestration")));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        var requirement = outcome.Requirements!.Requirements.Should().ContainSingle().Subject;
        requirement.Inferred.Should().BeTrue("an unverifiable quote is badged, never trusted");
        requirement.Text.Should().Be("kubernetes", "and never silently stripped");
    }

    [Fact]
    public async Task Missing_evidence_span_marks_the_requirement_inferred()
    {
        var chat = new FakeChatClient(() => Reply(RequirementWithSpan(null)));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        outcome.Requirements!.Requirements.Single().Inferred.Should().BeTrue();
    }

    [Fact]
    public async Task Model_declared_inferred_is_never_downgraded_by_a_matching_span()
    {
        var chat = new FakeChatClient(() => Reply(
            """
            {"requirements":[{"text":"kubernetes","kind":"Skill","priority":"NiceToHave",
              "minYears":null,"evidenceSpan":"Kubernetes experience is a plus","inferred":true}],
             "seniority":"Senior","location":"Amsterdam","ambiguities":[]}
            """));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        outcome.Requirements!.Requirements.Single().Inferred.Should().BeTrue(
            "the model's own uncertainty admission is kept even when the quote verifies");
    }

    [Fact]
    public async Task Unparseable_reply_degrades_to_a_fault_with_the_metered_reply_intact()
    {
        var chat = new FakeChatClient(() => Reply("Sorry, here is an essay instead.", input: 80, output: 20));
        var extractor = new JdRequirementExtractor(chat);

        var outcome = await extractor.ExtractAsync(Jd);

        outcome.Requirements.Should().BeNull();
        outcome.FaultDetail.Should().NotBeNullOrWhiteSpace();
        outcome.Reply.InputTokens.Should().Be(80, "tokens were spent either way");
    }

    private static string RequirementWithSpan(string? span)
    {
        var evidence = span is null ? "null" : $"\"{span}\"";
        return $$"""
            {"requirements":[{"text":"kubernetes","kind":"Skill","priority":"NiceToHave",
              "minYears":null,"evidenceSpan":{{evidence}},"inferred":false}],
             "seniority":"Senior","location":"Amsterdam","ambiguities":[]}
            """;
    }

    private static ChatResponse Reply(
        string text, long input = 10, long output = 5, string? model = null)
        => new(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = input,
                OutputTokenCount = output,
                TotalTokenCount = input + output,
            },
            ModelId = model,
        };
}
