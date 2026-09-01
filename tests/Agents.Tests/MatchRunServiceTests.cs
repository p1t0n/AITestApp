using ExpertToJob.Agents.Agents;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Seam-level tests for <see cref="MatchRunService"/>: the extracted core of POST /agents/match.
/// A recording fake stands in for the agent, so the tests pin the two things the extraction moved
/// out of the endpoint — the prompt template (now the service's single source of truth) and the
/// typed outcome (answer + reply) the shell meters and returns from.
/// </summary>
public class MatchRunServiceTests
{
    private sealed class RecordingChatAgent(string reply = "Fit: MODERATE (60/100)") : IChatAgent
    {
        public string? LastQuestion { get; private set; }

        public string Name => "match";

        public Task<AgentReply> AskAsync(string question, CancellationToken ct = default)
        {
            LastQuestion = question;
            return Task.FromResult(new AgentReply(reply, 123, 45, 168));
        }
    }

    [Fact]
    public async Task Builds_the_pinned_prompt_from_the_employee_id_and_job_description()
    {
        var agent = new RecordingChatAgent();
        var employeeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await new MatchRunService(agent).RunAsync(employeeId, "Senior React engineer, GraphQL.");

        agent.LastQuestion.Should().Be(
            "Assess employee 11111111-1111-1111-1111-111111111111 against this job description:\n\nSenior React engineer, GraphQL.");
    }

    [Fact]
    public async Task Returns_the_agents_answer_and_the_reply_the_shell_meters_from()
    {
        var run = await new MatchRunService(new RecordingChatAgent())
            .RunAsync(Guid.NewGuid(), "Senior React engineer.");

        run.AgentName.Should().Be("match");
        run.Answer.Should().Be("Fit: MODERATE (60/100)");
        run.Reply.TotalTokens.Should().Be(168);
    }

    [Fact]
    public async Task Appends_the_extraction_prompt_block_when_the_caller_provides_one()
    {
        var agent = new RecordingChatAgent();
        var extraction = new JdRequirements(
            [new JdRequirement("event streaming", RequirementKind.Skill, RequirementPriority.MustHave,
                5, "event streaming", Inferred: false)],
            JdSeniority.Senior, "Amsterdam", ["Budget unclear"]);

        await new MatchRunService(agent).RunAsync(Guid.NewGuid(), "JD text.", extraction);

        agent.LastQuestion.Should().Contain("Extracted role requirements")
            .And.Contain("[MustHave/Skill] event streaming, min 5 yrs")
            .And.Contain("Seniority: Senior; Location: Amsterdam")
            .And.Contain("Budget unclear");
    }

    [Fact]
    public async Task Parses_the_structured_verdict_into_markdown_score_and_band()
    {
        var run = await new MatchRunService(new RecordingChatAgent(
                """{"score":91,"band":"Strong","gapAnalysisMarkdown":"## Gap analysis\n\nAll met."}"""))
            .RunAsync(Guid.NewGuid(), "Senior React engineer.");

        run.Answer.Should().Be("## Gap analysis\n\nAll met.");
        run.Score.Should().Be(91);
        run.Band.Should().Be("Strong");
    }

    [Fact]
    public async Task Maps_InsufficientEvidence_to_the_legacy_display_band()
    {
        var run = await new MatchRunService(new RecordingChatAgent(
                """{"score":null,"band":"InsufficientEvidence","gapAnalysisMarkdown":"No usable CV evidence."}"""))
            .RunAsync(Guid.NewGuid(), "Senior React engineer.");

        run.Band.Should().Be("Insufficient evidence", "report/UI contracts keep the parser-era string");
        run.Score.Should().BeNull("honest absence stays null, never invented");
    }

    [Fact]
    public async Task Falls_back_to_the_regex_parser_on_a_non_json_reply()
    {
        var run = await new MatchRunService(new RecordingChatAgent(
                "## Fit assessment\n\nOverall score: 60/100\nOverall band: Moderate"))
            .RunAsync(Guid.NewGuid(), "Senior React engineer.");

        run.Answer.Should().Contain("Fit assessment", "the raw markdown ships when there is no JSON");
        run.Score.Should().Be(60);
        run.Band.Should().Be("Moderate");
    }

    [Fact]
    public async Task Out_of_range_structured_score_is_dropped_not_trusted()
    {
        var run = await new MatchRunService(new RecordingChatAgent(
                """{"score":780,"band":"Strong","gapAnalysisMarkdown":"Analysis."}"""))
            .RunAsync(Guid.NewGuid(), "Senior React engineer.");

        run.Score.Should().BeNull();
        run.Band.Should().Be("Strong");
    }
}
