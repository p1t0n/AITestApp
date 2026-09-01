using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The ingestion run service composes the response strictly from captured tool results: created
/// counts, degradation notes for exhausted failures (the P1T-80 children-degrade ladder), the
/// duplicate warning, and the abort path when no draft was created (core-abort ladder rung).
/// Proposals are the one model-sourced field, parsed from the minimal closing JSON.
/// </summary>
public class ResumeIngestionRunServiceTests
{
    private static readonly Guid DraftId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static string DraftPayload() =>
        $$"""{"employee":{"id":"{{DraftId}}"},"duplicateWarning":"Same name exists."}""";

    private const string ValidationError =
        """{"code":"validation_failed","message":"Validation failed.","fields":[]}""";

    private static FunctionCallContent Call(string id, string name) => new(id, name, new Dictionary<string, object?>());

    private static AIFunction Tool(string name, Func<string> result) =>
        AIFunctionFactory.Create(() => result(), name);

    private static ResumeIngestionRunService Service(FakeChatClient chat, params AITool[] tools) =>
        new(new ResumeIngestionAgent(chat, new FakeToolSource(tools), NullLoggerFactory.Instance));

    [Fact]
    public async Task Composes_counts_proposals_and_duplicate_warning_from_captures()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [Call("c2", "language_add"), Call("c3", "employee_skill_add"), Call("c4", "experience_add"), Call("c5", "experience_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":["LabVIEW","COBOL"],"aborted":false,"abortReason":null}""")));

        var outcome = await Service(chat,
                Tool("employee_create_draft", DraftPayload),
                Tool("language_add", () => """{"id":"a"}"""),
                Tool("employee_skill_add", () => """{"id":"b"}"""),
                Tool("experience_add", () => """{"id":"c"}"""))
            .RunAsync("resume");

        outcome.Response.Should().NotBeNull();
        var r = outcome.Response!;
        r.EmployeeId.Should().Be(DraftId);
        r.Created.Should().Be(new IngestionCreated(Languages: 1, Skills: 1, Qualifications: 0, Experiences: 2));
        r.Proposals.Should().Equal("LabVIEW", "COBOL");
        r.DuplicateWarning.Should().Be("Same name exists.");
        r.Notes.Should().BeEmpty();
        r.Degraded.Should().BeFalse();
        outcome.AbortDetail.Should().BeNull();
    }

    [Fact]
    public async Task A_child_that_keeps_failing_degrades_into_a_note_not_an_abort()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c2", "qualification_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c3", "qualification_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":false,"abortReason":null}""")));

        var outcome = await Service(chat,
                Tool("employee_create_draft", DraftPayload),
                Tool("qualification_add", () => ValidationError))
            .RunAsync("resume");

        outcome.Response.Should().NotBeNull("child failures degrade, they never abort the run");
        var r = outcome.Response!;
        r.Created.Qualifications.Should().Be(0);
        r.Degraded.Should().BeTrue();
        r.Notes.Should().ContainSingle(n => n.Contains("qualification_add") && n.Contains("validation_failed"));
    }

    [Fact]
    public async Task Retry_that_eventually_succeeds_counts_cleanly_without_degrading()
    {
        var calls = 0;
        var qualification = AIFunctionFactory.Create(
            () => ++calls == 1 ? ValidationError : """{"id":"q1"}""", "qualification_add");
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c2", "qualification_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c3", "qualification_add")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":false,"abortReason":null}""")));

        var outcome = await Service(chat, Tool("employee_create_draft", DraftPayload), qualification)
            .RunAsync("resume");

        outcome.Response!.Created.Qualifications.Should().Be(1, "the corrected retry succeeded");
        outcome.Response.Notes.Should().BeEmpty("a covered failure is self-correction, not degradation");
        outcome.Response.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task Aborts_with_the_create_error_when_no_draft_was_created()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call("c1", "employee_create_draft")])),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"proposals":[],"aborted":true,"abortReason":"No name in the text."}""")));

        var outcome = await Service(chat, Tool("employee_create_draft", () => ValidationError))
            .RunAsync("garbage");

        outcome.Response.Should().BeNull();
        outcome.AbortDetail.Should().Contain("validation_failed");
    }

    [Fact]
    public async Task Aborts_with_the_models_reason_when_it_never_even_tried_to_create()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "```json\n{\"proposals\":[],\"aborted\":true,\"abortReason\":\"The text is not a resume.\"}\n```")));

        var outcome = await Service(chat, Tool("employee_create_draft", DraftPayload))
            .RunAsync("what is the weather");

        outcome.Response.Should().BeNull();
        outcome.AbortDetail.Should().Be("The text is not a resume.", "fenced closing JSON must still parse");
    }
}
