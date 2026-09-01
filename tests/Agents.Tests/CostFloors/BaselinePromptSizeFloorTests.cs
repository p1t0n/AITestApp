using System.Reflection;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Mcp;
using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.CostFloors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests.CostFloors;

/// <summary>
/// The agent half of the deterministic Cost Floors (P1T-144): Baseline Prompt Size — what one
/// model call costs an agent before a single tool result comes back. Turn Amplification multiplies
/// it by every iteration, so 4,202 tokens of instructions-plus-schemas became 42,020 (26%) of one
/// roster-qa run. No model is involved here either: the prompt and the tool list ARE the
/// measurement — and since P1T-146 the tool list is the agent's Tool Allowlist, not the whole
/// surface its scope carries.
/// </summary>
public class BaselinePromptSizeFloorTests(ITestOutputHelper output)
{
    /// <summary>Every agent that authors an <c>Instructions</c> prompt, by class.</summary>
    private static readonly Type[] PromptedAgents =
    [
        typeof(RosterQaAgent),
        typeof(CvTailoringAgent),
        typeof(ResumeIngestionAgent),
        typeof(MatchAgent),
        typeof(ShortlistAgent),
        typeof(InterviewKitAgent),
        typeof(BenchReportService),
        typeof(JdRequirementExtractor),
        typeof(QueuedSyncScoringTransport),
    ];

    [Fact]
    public void Every_agents_instructions_stay_under_their_ratcheted_ceiling()
    {
        using var _ = new AssertionScope();
        foreach (var type in PromptedAgents)
        {
            var tokens = TokenEstimate.Of(InstructionsOf(type));
            output.WriteLine($"{type.Name,-28} {tokens,5} instruction tokens");

            ExpertToJob.CostFloors.CostFloors.AgentInstructionCeilings.Should().ContainKey(type.Name);
            if (ExpertToJob.CostFloors.CostFloors.AgentInstructionCeilings.TryGetValue(type.Name, out var ceiling))
            {
                tokens.Should().BeLessThanOrEqualTo(
                    ceiling, $"{type.Name}'s instructions are re-sent on every iteration it runs");
            }
        }
    }

    [Fact]
    public void The_ratchet_table_names_no_agent_that_no_longer_exists()
    {
        // A ceiling for a deleted agent is a floor guarding nothing — and it would quietly make
        // the coverage assertion above pass for the wrong reason.
        ExpertToJob.CostFloors.CostFloors.AgentInstructionCeilings.Keys
            .Should().BeSubsetOf(PromptedAgents.Select(t => t.Name));
    }

    [Theory]
    [MemberData(nameof(ToolLoopingAgents))]
    public async Task Baseline_prompt_size_stays_under_its_ratcheted_ceiling(
        string agentName, string configKey, Func<IChatClient, IMcpToolSource, Task> run)
    {
        // Exactly the surface the agent's own identity would hand it: its Tool Allowlist, which
        // McpToolSource applies before any agent sees a tool (P1T-146). Measuring against the
        // whole scope surface would be measuring fiction now that no agent is offered it. Names
        // only: the schema sizes come from the Mcp.Tests floor, which is what holds them true.
        var offered = ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists[configKey]
            .Select(name => (AITool)AIFunctionFactory.Create(() => "{}", name))
            .ToArray();
        var chat = new FakeChatClient(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));

        await run(chat, new FakeToolSource(offered));

        // The most expensive iteration, not the first: a two-turn agent may widen its tool set
        // partway through, and Baseline Prompt Size is what a call costs at its worst.
        var shown = chat.ReceivedOptions
            .Select(o => o?.Tools?.Select(t => t.Name).ToList() ?? [])
            .OrderByDescending(names => ExpertToJob.CostFloors.CostFloors.BaselinePromptSize(0, names))
            .First();
        var instructions = TokenEstimate.Of(InstructionsOf(PromptedAgents.Single(t => t.Name == agentName)));
        var baseline = ExpertToJob.CostFloors.CostFloors.BaselinePromptSize(instructions, shown);
        output.WriteLine(
            $"{agentName,-24} {baseline,5} tokens = {instructions} instructions + " +
            $"{shown.Count} tool schemas ({string.Join(", ", shown)})");

        baseline.Should().BeLessThanOrEqualTo(
            ExpertToJob.CostFloors.CostFloors.BaselinePromptSizeCeilings[agentName],
            $"{agentName} pays this on every iteration, before any tool result");
    }

    public static TheoryData<string, string, Func<IChatClient, IMcpToolSource, Task>> ToolLoopingAgents() => new()
    {
        {
            nameof(RosterQaAgent), "roster-qa",
            (chat, tools) => new RosterQaAgent(chat, tools, NullLoggerFactory.Instance).AskAsync("q")
        },
        {
            nameof(CvTailoringAgent), "cv-tailoring",
            (chat, tools) => new CvTailoringAgent(chat, tools, NullLoggerFactory.Instance)
                .TailorAsync(Guid.NewGuid(), "job description")
        },
        {
            // The one agent holding mcp:write, and the one with the worst recorded call (P1T-150).
            nameof(ResumeIngestionAgent), "resume-ingestion",
            (chat, tools) => new ResumeIngestionAgent(chat, tools, NullLoggerFactory.Instance).IngestAsync("resume")
        },
        {
            nameof(MatchAgent), "match",
            (chat, tools) => new MatchAgent(chat, tools, NullLoggerFactory.Instance).AskAsync("q")
        },
        {
            nameof(InterviewKitAgent), "interview-kit",
            (chat, tools) => new InterviewKitAgent(chat, tools, NullLoggerFactory.Instance).GenerateAsync("q")
        },
    };

    /// <summary>The authored prompt itself. Read off the private const rather than through a
    /// widened API: the prompt is an implementation detail everywhere except here, and a rename
    /// failing this test loudly is the right outcome — the ceiling has to move with it.</summary>
    private static string InstructionsOf(Type agent) =>
        agent.GetField("Instructions", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetRawConstantValue() as string
        ?? throw new InvalidOperationException($"{agent.Name} has no private const string Instructions.");
}
