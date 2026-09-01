using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Deterministic tests for the shortlist rationale generator (tool-less since P1T-117): the
/// schema-constrained request shape, the evidence-grounded prompt, and the reply passthrough the
/// composer parses. No live model.
/// </summary>
public class ShortlistAgentTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ShortlistToolPayload Payload() => new(
    [
        new ShortlistToolCandidate(Ada, "Ada Lovelace", "Platform Lead", 0.91, 2, 3,
        [
            new ShortlistToolEvidence("event streaming with Kafka", true, "Built Kafka pipelines.", 0.88),
            new ShortlistToolEvidence("team leadership", false),
        ]),
    ]);

    [Fact]
    public async Task Requests_the_structured_rationales_schema_on_the_wire()
    {
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"rationales":[{"expertId":"{{Ada}}","rationale":"Strong Kafka evidence."}]}""")));
        var agent = new ShortlistAgent(chat);

        await agent.RationalesAsync("Platform engineer JD.", Payload());

        chat.ReceivedOptions[0]!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>()
            .Which.Schema.Should().NotBeNull();
        chat.ReceivedOptions[0]!.Tools.Should().BeNullOrEmpty("the rationale call is tool-less");
    }

    [Fact]
    public async Task Prompt_carries_the_jd_and_the_per_requirement_evidence()
    {
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"rationales":[]}""")));
        var agent = new ShortlistAgent(chat);

        await agent.RationalesAsync("Platform engineer JD.", Payload());

        var prompt = string.Concat(chat.ReceivedMessages[0].Select(m => m.Text));
        prompt.Should().Contain("Platform engineer JD.")
            .And.Contain("Ada Lovelace")
            .And.Contain("event streaming with Kafka")
            .And.Contain("Built Kafka pipelines.");
    }

    [Fact]
    public async Task Returns_the_reply_text_and_usage_for_the_caller_to_meter()
    {
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"rationales":[]}"""))
        {
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30, TotalTokenCount = 150 },
            ModelId = "gemini-3.5-flash-lite",
        });
        var agent = new ShortlistAgent(chat);

        var reply = await agent.RationalesAsync("JD.", Payload());

        reply.Text.Should().Be("""{"rationales":[]}""");
        reply.TotalTokens.Should().Be(150);
        reply.ModelId.Should().Be("gemini-3.5-flash-lite");
        agent.Name.Should().Be("shortlist");
    }
}
