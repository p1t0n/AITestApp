using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the CV Tailoring agent's 2-turn rewrite flow using a fake chat client
/// + fake MCP tools. No live model, no MCP server — these assert the wiring: the agent narrows
/// the tool surface to cv_get + style_exemplar_search, drives the two turns (tailoring markdown,
/// then rewrites JSON), and captures the cv_get result, the selected achievement ids, and the
/// exemplar payload so the endpoint composes rewrites from tool-sourced facts.
/// </summary>
public class CvTailoringAgentTests
{
    private static readonly Guid Achievement1 = Guid.Parse("aaaaaaa1-1111-1111-1111-111111111111");
    private static readonly Guid Achievement2 = Guid.Parse("aaaaaaa2-2222-2222-2222-222222222222");
    private static readonly Guid Experience1 = Guid.Parse("eeeeeee1-1111-1111-1111-111111111111");

    private const string CvPayload =
        """
        {"fullName":"Ada Lovelace","title":"Platform Lead","experiences":[{"id":"eeeeeee1-1111-1111-1111-111111111111","company":"Acme","title":"Senior Engineer","period":"Jan 2020 – Present","summary":"Platform work.","achievements":[{"id":"aaaaaaa1-1111-1111-1111-111111111111","text":"Cut deploy time 40%."},{"id":"aaaaaaa2-2222-2222-2222-222222222222","text":"Led migration to Kubernetes."}],"skills":["C#"]}]}
        """;

    private const string ExemplarPayload =
        """
        {"results":[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","exemplars":[{"text":"Reduced [company] settlement lag 55% by rebuilding the pipeline.","similarity":0.82}]}],"error":null}
        """;

    private const string AnswerMarkdown = "Tailored summary: Ada is a strong platform fit.\n\nEmphasise Kubernetes.";

    private const string RewritesJson =
        """[{"achievementId":"aaaaaaa1-1111-1111-1111-111111111111","rewritten":"Cut deploy time 40% by automating the release train."}]""";

    private static AIFunction CvGetTool(string? payload = null) =>
        AIFunctionFactory.Create((Guid employeeId) => payload ?? CvPayload, "cv_get");

    private static AIFunction ExemplarTool(Action? onInvoke = null, string? payload = null) =>
        AIFunctionFactory.Create(
            (Guid[] achievementIds) => { onInvoke?.Invoke(); return payload ?? ExemplarPayload; },
            "style_exemplar_search");

    private static AIFunction EmployeeListTool() =>
        AIFunctionFactory.Create(() => "Ada Lovelace;id-1", "employee_list");

    /// <summary>The scripted happy path: turn 1 calls cv_get then style_exemplar_search and
    /// answers with the tailoring markdown; turn 2 returns only the rewrites JSON.</summary>
    private static FakeChatClient ScriptedChat() => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "cv_get",
                new Dictionary<string, object?> { ["employeeId"] = Guid.NewGuid() })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-2", "style_exemplar_search",
                new Dictionary<string, object?>
                {
                    ["achievementIds"] = new[] { Achievement1.ToString(), Achievement2.ToString() },
                })])),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, AnswerMarkdown)),
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, RewritesJson)));

    [Fact]
    public async Task Exposes_only_cv_get_and_style_exemplar_search_to_the_model()
    {
        var chat = ScriptedChat();
        // The tool source offers more; the agent must narrow to exactly the two tailoring tools.
        var agent = new CvTailoringAgent(
            chat, new FakeToolSource(CvGetTool(), ExemplarTool(), EmployeeListTool()), NullLoggerFactory.Instance);

        await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        agent.Name.Should().Be("cv-tailoring");
        chat.ReceivedOptions.Should().NotBeEmpty();
        var tools = chat.ReceivedOptions[0]!.Tools;
        tools.Should().Contain(t => t.Name == "cv_get");
        tools.Should().Contain(t => t.Name == "style_exemplar_search");
        tools.Should().HaveCount(2, "nothing but cv_get and style_exemplar_search may reach the model");
    }

    [Fact]
    public async Task Separates_the_tailoring_answer_from_the_rewrites_json_across_the_two_turns()
    {
        var agent = new CvTailoringAgent(
            ScriptedChat(), new FakeToolSource(CvGetTool(), ExemplarTool()), NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        outcome.Reply.Text.Should().Be(AnswerMarkdown, "turn 1's text is the answer, exactly as today");
        outcome.RewritesText.Should().Be(RewritesJson, "turn 2 contributes only the rewrites JSON");
    }

    [Fact]
    public async Task Captures_the_selected_achievement_ids_the_exemplars_and_the_cv()
    {
        var exemplarInvoked = false;
        var agent = new CvTailoringAgent(
            ScriptedChat(),
            new FakeToolSource(CvGetTool(), ExemplarTool(() => exemplarInvoked = true)),
            NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        exemplarInvoked.Should().BeTrue("the agent should run the exemplar tool the model asked for");
        outcome.SelectedAchievementIds.Should().Equal(Achievement1, Achievement2);

        outcome.Exemplars.Should().NotBeNull("the exemplar result must be captured for the guard");
        outcome.Exemplars!.Error.Should().BeNull();
        var bullet = outcome.Exemplars.Results.Should().ContainSingle().Subject;
        bullet.AchievementId.Should().Be(Achievement1);
        var exemplar = bullet.Exemplars.Should().ContainSingle().Subject;
        exemplar.Text.Should().Be("Reduced [company] settlement lag 55% by rebuilding the pipeline.");
        exemplar.Similarity.Should().BeApproximately(0.82, 0.0001);

        outcome.Cv.Should().NotBeNull("originals and experience ids must come from the captured CV");
        var experience = outcome.Cv!.Experiences.Should().ContainSingle().Subject;
        experience.Id.Should().Be(Experience1);
        experience.Company.Should().Be("Acme");
        experience.Period.Should().Be("Jan 2020 – Present");
        experience.Achievements.Should().HaveCount(2);
        experience.Achievements[0].Id.Should().Be(Achievement1);
        experience.Achievements[0].Text.Should().Be("Cut deploy time 40%.");
    }

    [Fact]
    public async Task Captures_tool_results_that_arrive_as_TextContent_blocks()
    {
        // The real MCP tools (McpClientTool via the Agent Framework) hand the function result back
        // as a Microsoft.Extensions.AI.TextContent whose Text holds the payload JSON — it
        // serializes to {"$type":"text","text":"{…}"}, not a bare payload or an MCP content-array
        // envelope. Missing this shape was a production bug in the shortlist flow.
        var cvTool = AIFunctionFactory.Create((Guid employeeId) => new TextContent(CvPayload), "cv_get");
        var exemplarTool = AIFunctionFactory.Create(
            (Guid[] achievementIds) => new TextContent(ExemplarPayload), "style_exemplar_search");
        var agent = new CvTailoringAgent(
            ScriptedChat(), new FakeToolSource(cvTool, exemplarTool), NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        outcome.Cv.Should().NotBeNull("a TextContent-shaped cv_get result must still be captured");
        outcome.Cv!.Experiences.Should().ContainSingle().Which.Id.Should().Be(Experience1);
        outcome.Exemplars.Should().NotBeNull("a TextContent-shaped exemplar result must still be captured");
        outcome.Exemplars!.Results.Should().ContainSingle().Which.AchievementId.Should().Be(Achievement1);
    }

    [Fact]
    public async Task Captures_are_empty_when_the_model_never_calls_the_tools()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "That employee was not found.")),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]")));
        var agent = new CvTailoringAgent(
            chat, new FakeToolSource(CvGetTool(), ExemplarTool()), NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor a missing employee's CV.");

        outcome.Reply.Text.Should().Contain("not found");
        outcome.Cv.Should().BeNull();
        outcome.Exemplars.Should().BeNull();
        outcome.SelectedAchievementIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Captures_a_soft_error_from_the_exemplar_tool()
    {
        var agent = new CvTailoringAgent(
            ScriptedChat(),
            new FakeToolSource(CvGetTool(), ExemplarTool(
                payload: """{"results":[],"error":"The embedding backend is unavailable."}""")),
            NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        outcome.Exemplars.Should().NotBeNull();
        outcome.Exemplars!.Error.Should().Be("The embedding backend is unavailable.");
        outcome.Exemplars.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rewrite_turn_failure_degrades_to_the_answer_without_rewrites()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "cv_get",
                    new Dictionary<string, object?> { ["employeeId"] = Guid.NewGuid() })])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, AnswerMarkdown)),
            // Turn 2 (the rewrite request) faults at the model endpoint.
            () => throw new HttpRequestException("model endpoint unreachable"));
        var agent = new CvTailoringAgent(
            chat, new FakeToolSource(CvGetTool(), ExemplarTool()), NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        outcome.Reply.Text.Should().Be(AnswerMarkdown, "the answer the caller already earned must survive");
        outcome.RewritesText.Should().BeEmpty();
        outcome.Cv.Should().NotBeNull("the cv_get capture happened before the rewrite turn failed");
    }

    [Fact]
    public async Task Sums_token_usage_across_both_turns()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, AnswerMarkdown))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 40, TotalTokenCount = 140 },
            },
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]"))
            {
                Usage = new UsageDetails { InputTokenCount = 60, OutputTokenCount = 10, TotalTokenCount = 70 },
            });
        var agent = new CvTailoringAgent(
            chat, new FakeToolSource(CvGetTool(), ExemplarTool()), NullLoggerFactory.Instance);

        var outcome = await agent.TailorAsync("Tailor Ada's CV for a platform role.");

        outcome.Reply.InputTokens.Should().Be(160);
        outcome.Reply.OutputTokens.Should().Be(50);
        outcome.Reply.TotalTokens.Should().Be(210);
    }
}
