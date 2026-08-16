using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the Resume Ingestion agent (P1T-92) — fake chat client, fake MCP tools,
/// no live model. Assert the wiring: the narrowed write-tool surface, the captured draft id and
/// duplicate warning, the per-call success/failure record the run service composes from, and the
/// self-correction loop (a validation error flows back to the model, which retries).
/// </summary>
public class ResumeIngestionAgentTests
{
    private static readonly Guid DraftId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static string DraftPayload(string? duplicateWarning = null) =>
        $$"""
        {"employee":{"id":"{{DraftId}}","firstName":"Torvald","lastName":"Emberwright"},"duplicateWarning":{{(duplicateWarning is null ? "null" : $"\"{duplicateWarning}\"")}}}
        """;

    private const string ValidationError =
        """{"code":"validation_failed","message":"Validation failed.","fields":[{"field":"EndDate","message":"EndDate must be on or after StartDate."}]}""";

    private static AIFunction Tool(string name, Func<string> result) =>
        AIFunctionFactory.Create(() => result(), name);

    private static FunctionCallContent Call(string id, string name, Dictionary<string, object?>? args = null) =>
        new(id, name, args ?? []);

    [Fact]
    public async Task Requests_the_structured_closing_schema_on_the_wire()
    {
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"proposals":[],"aborted":false,"abortReason":null}""")));
        var agent = new ResumeIngestionAgent(chat, new FakeToolSource(
                Tool("employee_create_draft", () => DraftPayload()),
                Tool("skill_list", () => "[]")),
            NullLoggerFactory.Instance);

        await agent.IngestAsync("Resume.");

        chat.ReceivedOptions[0]!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>()
            .Which.Schema.Should().NotBeNull("the closing report is schema-constrained since P1T-118");
    }

    [Fact]
    public async Task Exposes_only_the_narrowed_write_surface_to_the_model()
    {
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"proposals":[],"aborted":true,"abortReason":"nothing to do"}""")));
        var agent = new ResumeIngestionAgent(chat, new FakeToolSource(
                Tool("employee_create_draft", () => DraftPayload()),
                Tool("language_add", () => "{}"),
                Tool("employee_skill_add", () => "{}"),
                Tool("qualification_add", () => "{}"),
                Tool("experience_add", () => "{}"),
                Tool("skill_list", () => "[]"),
                Tool("skill_create", () => "{}"),
                Tool("employee_create", () => "{}"),
                Tool("availability_add", () => "{}"),
                Tool("employee_delete", () => "{}")),
            NullLoggerFactory.Instance);

        await agent.IngestAsync("resume");

        agent.Name.Should().Be("resume-ingestion");
        var toolNames = chat.ReceivedOptions[0]!.Tools!.Select(t => t.Name).ToList();
        toolNames.Should().BeEquivalentTo(
            "employee_create_draft", "language_add", "employee_skill_add",
            "qualification_add", "experience_add", "skill_list");
    }

    [Fact]
    public async Task Captures_the_draft_id_duplicate_warning_and_every_write_call()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c2", "language_add"), Call("c3", "experience_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":["LabVIEW"],"aborted":false,"abortReason":null}""")));

        var agent = new ResumeIngestionAgent(chat, new FakeToolSource(
                Tool("employee_create_draft", () => DraftPayload("Same name exists.")),
                Tool("language_add", () => """{"id":"aaaaaaaa-1111-1111-1111-111111111111"}"""),
                Tool("experience_add", () => """{"id":"bbbbbbbb-1111-1111-1111-111111111111"}"""),
                Tool("skill_list", () => "[]")),
            NullLoggerFactory.Instance);

        var outcome = await agent.IngestAsync("resume text");

        outcome.EmployeeId.Should().Be(DraftId);
        outcome.DuplicateWarning.Should().Be("Same name exists.");
        outcome.ToolCalls.Should().HaveCount(3);
        outcome.ToolCalls.Select(c => c.Tool).Should().Equal(
            "employee_create_draft", "language_add", "experience_add");
        outcome.ToolCalls.Should().OnlyContain(c => c.Succeeded);
        outcome.ClosingJson.Should().Contain("LabVIEW");
    }

    [Fact]
    public async Task Validation_error_flows_back_and_the_retry_is_recorded_as_selfcorrection()
    {
        // experience_add fails once (invalid dates) then succeeds on the model's corrected retry.
        var experienceCalls = 0;
        var experienceTool = AIFunctionFactory.Create(
            () => ++experienceCalls == 1
                ? ValidationError
                : """{"id":"bbbbbbbb-1111-1111-1111-111111111111"}""",
            "experience_add");

        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c2", "experience_add")])),
            // The model sees the validation error and retries with fixed arguments.
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c3", "experience_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":false,"abortReason":null}""")));

        var agent = new ResumeIngestionAgent(chat, new FakeToolSource(
                Tool("employee_create_draft", () => DraftPayload()),
                experienceTool,
                Tool("skill_list", () => "[]")),
            NullLoggerFactory.Instance);

        var outcome = await agent.IngestAsync("resume text");

        experienceCalls.Should().Be(2, "the error must reach the model so it can self-correct");
        outcome.ToolCalls.Should().HaveCount(3);
        outcome.ToolCalls[1].Succeeded.Should().BeFalse();
        outcome.ToolCalls[1].Error.Should().Contain("validation_failed");
        outcome.ToolCalls[2].Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task EmployeeId_is_null_when_the_draft_create_never_succeeds()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":true,"abortReason":"The resume has no name."}""")));

        var agent = new ResumeIngestionAgent(chat, new FakeToolSource(
                Tool("employee_create_draft", () => ValidationError),
                Tool("skill_list", () => "[]")),
            NullLoggerFactory.Instance);

        var outcome = await agent.IngestAsync("garbage");

        outcome.EmployeeId.Should().BeNull();
        outcome.ToolCalls.Should().ContainSingle(c => c.Tool == "employee_create_draft" && !c.Succeeded);
    }

    [Fact]
    public async Task Captures_a_draft_result_that_arrives_as_a_TextContent_block()
    {
        // The real MCP tool hands results back as TextContent — same production shape the
        // shortlist capture had to learn the hard way.
        var tool = AIFunctionFactory.Create(() => new TextContent(DraftPayload()), "employee_create_draft");
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":false,"abortReason":null}""")));

        var agent = new ResumeIngestionAgent(
            chat, new FakeToolSource(tool, Tool("skill_list", () => "[]")), NullLoggerFactory.Instance);

        var outcome = await agent.IngestAsync("resume text");

        outcome.EmployeeId.Should().Be(DraftId);
    }
}
