using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Seam-level tests for <see cref="ShortlistRunService"/>: the extracted core of
/// POST /agents/shortlist. Driven by a real <see cref="ShortlistAgent"/> over a fake chat client
/// and fake tool source, they prove the extraction is complete — the service produces the same
/// composed response (including the templated-rationale degrade and the corruption guard) and the
/// same upstream-fault outcomes the endpoint used to produce inline, plus the reply the shell
/// needs for metering.
/// </summary>
public class ShortlistRunServiceTests
{
    private const string AdaIdText = "11111111-1111-1111-1111-111111111111";

    private const string ToolPayload =
        """
        {"results":[{"employeeId":"11111111-1111-1111-1111-111111111111","name":"Ada Lovelace","title":"Platform Lead","score":0.91,"matchedCount":2,"totalRequirements":3,"evidence":[{"requirement":"event streaming with Kafka","matched":true,"snippet":"Built Kafka pipelines.","similarity":0.88},{"requirement":"Kubernetes operations","matched":true,"snippet":"Ran K8s clusters.","similarity":0.8},{"requirement":"team leadership","matched":false}]}],"error":null}
        """;

    private static AIFunction ShortlistTool(string? payload = null) =>
        AIFunctionFactory.Create((string[] requirements) => payload ?? ToolPayload, "roster_shortlist_search");

    private static FakeChatClient ScriptedChat() => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "roster_shortlist_search",
                new Dictionary<string, object?>
                {
                    ["requirements"] = new[] { "event streaming with Kafka", "Kubernetes operations", "team leadership" },
                })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""[{"employeeId":"{{AdaIdText}}","rationale":"Strong Kafka and K8s evidence."}]"""))
        {
            Usage = new UsageDetails { InputTokenCount = 123, OutputTokenCount = 45, TotalTokenCount = 168 },
        });

    private static ShortlistRunService Service(IChatClient chat, AIFunction? tool = null) =>
        new(new ShortlistAgent(chat, new FakeToolSource(tool ?? ShortlistTool()), NullLoggerFactory.Instance));

    [Fact]
    public async Task Composes_the_response_from_the_captured_tool_result_and_surfaces_the_reply_for_metering()
    {
        var run = await Service(ScriptedChat()).RunAsync(
            new ShortlistAgentRequest("Platform engineer: Kafka, Kubernetes, leadership.", TopK: 5));

        run.AgentName.Should().Be("shortlist");
        run.FaultDetail.Should().BeNull();
        run.Reply.TotalTokens.Should().Be(168, "the shell meters from the surfaced reply");

        run.Response.Should().NotBeNull();
        run.Response!.Requirements.Should().Equal(
            "event streaming with Kafka", "Kubernetes operations", "team leadership");
        var ada = run.Response.Candidates.Should().ContainSingle().Subject;
        ada.EmployeeId.Should().Be(Guid.Parse(AdaIdText));
        ada.Name.Should().Be("Ada Lovelace");
        ada.Title.Should().Be("Platform Lead");
        ada.Score.Should().BeApproximately(0.91, 0.0001);
        ada.Coverage.Should().Be(new ShortlistCoverage(2, 3));
        ada.Rationale.Should().Be("Strong Kafka and K8s evidence.");
        ada.Requirements.Should().HaveCount(3);
        ada.Requirements[0].Snippet.Should().Be("Built Kafka pipelines.");
        ada.Requirements[2].Matched.Should().BeFalse();
    }

    [Fact]
    public async Task Degrades_to_a_templated_rationale_when_the_model_returns_prose()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "roster_shortlist_search",
                    new Dictionary<string, object?> { ["requirements"] = new[] { "Kafka" } })])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Ada seems like a great fit for this role!")));

        var run = await Service(chat).RunAsync(new ShortlistAgentRequest("Platform engineer."));

        run.Response.Should().NotBeNull("unparseable model prose must not fail the run");
        run.Response!.Candidates[0].Rationale.Should().Be(
            "Matched 2/3 requirements: event streaming with Kafka, Kubernetes operations; missing: team leadership.");
    }

    [Fact]
    public async Task Reports_an_upstream_fault_when_the_model_never_calls_the_tool()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "No tool needed, trust me.")));

        var run = await Service(chat).RunAsync(new ShortlistAgentRequest("Platform engineer."));

        run.Response.Should().BeNull();
        run.FaultDetail.Should().Be("The agent did not produce a roster_shortlist_search result.");
    }

    [Fact]
    public async Task Reports_an_upstream_fault_when_the_tool_returns_a_soft_retrieval_error()
    {
        var run = await Service(
                ScriptedChat(),
                ShortlistTool("""{"results":[],"error":"The semantic search backend is unavailable."}"""))
            .RunAsync(new ShortlistAgentRequest("Platform engineer."));

        run.Response.Should().BeNull();
        run.FaultDetail.Should().Be("The semantic search backend is unavailable.");
    }
}
