using CvManager.Agents.Agents;
using FluentAssertions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Seam-level tests for <see cref="MatchRunService"/>: the extracted core of POST /agents/match.
/// A recording fake stands in for the agent, so the tests pin the two things the extraction moved
/// out of the endpoint — the prompt template (now the service's single source of truth) and the
/// typed outcome (answer + reply) the shell meters and returns from.
/// </summary>
public class MatchRunServiceTests
{
    private sealed class RecordingChatAgent : IChatAgent
    {
        public string? LastQuestion { get; private set; }

        public string Name => "match";

        public Task<AgentReply> AskAsync(string question, CancellationToken ct = default)
        {
            LastQuestion = question;
            return Task.FromResult(new AgentReply("Fit: MODERATE (60/100)", 123, 45, 168));
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
}
