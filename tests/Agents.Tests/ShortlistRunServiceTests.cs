using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Tests;

/// <summary>
/// Seam-level tests for <see cref="ShortlistRunService"/> (P1T-117 orchestration): extraction →
/// deterministic retrieval → rationales. Pins that the retrieval receives the extractor's texts
/// verbatim, that extraction/retrieval faults degrade as data with the extraction reply still
/// metered, and that the composed response carries the full extraction additively.
/// </summary>
public class ShortlistRunServiceTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeExtractor(JdExtractionOutcome outcome) : IJdRequirementExtractor
    {
        public string? LastJd { get; private set; }

        public Task<JdExtractionOutcome> ExtractAsync(string jobDescription, CancellationToken ct = default)
        {
            LastJd = jobDescription;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeSearch(ShortlistToolPayload? payload) : IShortlistSearch
    {
        public IReadOnlyList<string>? LastRequirements { get; private set; }
        public ShortlistAgentRequest? LastRequest { get; private set; }

        public Task<ShortlistToolPayload?> SearchAsync(
            IReadOnlyList<string> requirements, ShortlistAgentRequest request, CancellationToken ct = default)
        {
            LastRequirements = requirements;
            LastRequest = request;
            return Task.FromResult(payload);
        }
    }

    private static JdRequirements Extraction(params string[] texts) => new(
        texts.Select(t => new JdRequirement(t, RequirementKind.Skill, RequirementPriority.MustHave,
            null, EvidenceSpan: t, Inferred: false)).ToList(),
        JdSeniority.Senior,
        "Amsterdam",
        []);

    private static JdExtractionOutcome ExtractionOk(params string[] texts) => new(
        "jd-extraction", new AgentReply("{}", 90, 30, 120), Extraction(texts), FaultDetail: null);

    private static ShortlistToolPayload Payload() => new(
    [
        new ShortlistToolCandidate(Ada, "Ada Lovelace", "Platform Lead", 0.91, 1, 1,
            [new ShortlistToolEvidence("kafka", true, "Built Kafka pipelines.", 0.9)]),
    ]);

    private static ShortlistAgent RationaleAgent(out FakeChatClient chat)
    {
        chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"rationales":[{"employeeId":"{{Ada}}","rationale":"Strong Kafka evidence."}]}"""))
        {
            Usage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 10, TotalTokenCount = 60 },
        });
        return new ShortlistAgent(chat);
    }

    [Fact]
    public async Task Passes_the_extracted_requirement_texts_to_the_retrieval_verbatim()
    {
        var search = new FakeSearch(Payload());
        var service = new ShortlistRunService(
            new FakeExtractor(ExtractionOk("kafka", "kubernetes")), search, RationaleAgent(out _));
        var request = new ShortlistAgentRequest("JD text", TopK: 4);

        var run = await service.RunAsync(request);

        run.FaultDetail.Should().BeNull();
        search.LastRequirements.Should().Equal("kafka", "kubernetes");
        search.LastRequest.Should().BeSameAs(request, "filters pass through untouched");
    }

    [Fact]
    public async Task Composes_the_response_with_the_extraction_attached_and_both_replies()
    {
        var service = new ShortlistRunService(
            new FakeExtractor(ExtractionOk("kafka")), new FakeSearch(Payload()), RationaleAgent(out _));

        var run = await service.RunAsync(new ShortlistAgentRequest("JD text"));

        run.Response!.Requirements.Should().Equal("kafka");
        run.Response.Candidates.Should().ContainSingle()
            .Which.Rationale.Should().Be("Strong Kafka evidence.");
        run.Response.Extraction!.Seniority.Should().Be(JdSeniority.Senior);
        run.Reply.TotalTokens.Should().Be(60, "the shortlist-attributed reply is the rationale call");
        run.ExtractionReply!.TotalTokens.Should().Be(120, "extraction tokens are metered separately");
    }

    [Fact]
    public async Task Dedupes_blank_and_repeated_requirement_texts_and_caps_at_eight()
    {
        var texts = new[] { "kafka", "KAFKA", " ", "a", "b", "c", "d", "e", "f", "g", "h" };
        var search = new FakeSearch(Payload());
        var service = new ShortlistRunService(
            new FakeExtractor(ExtractionOk(texts)), search, RationaleAgent(out _));

        await service.RunAsync(new ShortlistAgentRequest("JD text"));

        search.LastRequirements.Should().HaveCount(8).And.StartWith("kafka");
    }

    [Fact]
    public async Task Extraction_fault_degrades_as_data_with_the_extraction_reply_intact()
    {
        var faulted = new JdExtractionOutcome(
            "jd-extraction", new AgentReply("essay", 80, 20, 100), Requirements: null, "did not parse");
        var search = new FakeSearch(Payload());
        var service = new ShortlistRunService(new FakeExtractor(faulted), search, RationaleAgent(out var chat));

        var run = await service.RunAsync(new ShortlistAgentRequest("JD text"));

        run.Response.Should().BeNull();
        run.FaultDetail.Should().Be("did not parse");
        run.ExtractionReply!.TotalTokens.Should().Be(100);
        run.Reply.TotalTokens.Should().Be(0, "no shortlist model call happened");
        search.LastRequirements.Should().BeNull("retrieval never ran");
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Retrieval_soft_error_degrades_as_data_before_the_rationale_call()
    {
        var service = new ShortlistRunService(
            new FakeExtractor(ExtractionOk("kafka")),
            new FakeSearch(new ShortlistToolPayload([], "embedding backend down")),
            RationaleAgent(out var chat));

        var run = await service.RunAsync(new ShortlistAgentRequest("JD text"));

        run.Response.Should().BeNull();
        run.FaultDetail.Should().Be("embedding backend down");
        chat.CallCount.Should().Be(0, "no rationale call for a failed retrieval");
    }

    [Fact]
    public async Task Unreadable_tool_result_degrades_as_data()
    {
        var service = new ShortlistRunService(
            new FakeExtractor(ExtractionOk("kafka")), new FakeSearch(null), RationaleAgent(out _));

        var run = await service.RunAsync(new ShortlistAgentRequest("JD text"));

        run.Response.Should().BeNull();
        run.FaultDetail.Should().Contain("unreadable");
    }
}
