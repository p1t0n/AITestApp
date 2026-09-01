using ExpertToJob.CostFloors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests.CostFloors;

/// <summary>
/// The Convergence half of the deterministic Cost Floors (P1T-148): not what one call costs, but
/// how many calls the run takes and what the whole run therefore costs.
///
/// <para>The two are genuinely different defects. P1T-144's floors would have caught a tool
/// description doubling; none of them would have caught the traced roster-qa run, which reached
/// *"three people in London, none know React"* through nine tool calls — three near-identical
/// semantic searches, a whole-roster <c>employee_list</c>, three speculative <c>cv_get</c>s, and a
/// shortlist search fired after it already had the answer. Every individual payload was inside its
/// ceiling. The shape of the run was the problem.</para>
///
/// <para>No model runs here either. The Convergent Path is declared, and priced out of ceilings
/// that <c>Agents.Tests</c> and <c>Mcp.Tests</c> already hold true — so this floor moves on its own
/// when they ratchet, and a change that makes the path longer is a red test rather than a bill.</para>
/// </summary>
public class ConvergenceCostFloorTests(ITestOutputHelper output)
{
    private const string Agent = nameof(ExpertToJob.Agents.Agents.RosterQaAgent);

    [Fact]
    public void The_reference_question_converges_within_its_iteration_ratchet()
    {
        // n tools is n+1 model calls: the closing call that writes the answer is an iteration too.
        var modelCalls = ExpertToJob.CostFloors.CostFloors.RosterQaConvergentPath.Count + 1;
        output.WriteLine(
            $"convergent path: {string.Join(" → ", ExpertToJob.CostFloors.CostFloors.RosterQaConvergentPath)} → answer " +
            $"({modelCalls} model calls; the traced run took 10)");

        modelCalls.Should().BeLessThanOrEqualTo(
            ExpertToJob.CostFloors.CostFloors.RosterQaConvergentRunIterationCeiling,
            "a longer path is Turn Amplification applied to every payload already in hand");
    }

    [Fact]
    public void Every_step_of_the_convergent_path_is_a_tool_the_agent_is_actually_shown()
    {
        // A path naming a tool roster-qa's token does not carry is fiction, and would quietly
        // price a run that cannot happen.
        ExpertToJob.CostFloors.CostFloors.RosterQaConvergentPath
            .Should().BeSubsetOf(ExpertToJob.CostFloors.CostFloors.ReadScopeTools);
    }

    [Fact]
    public void The_convergent_run_stays_under_its_ratcheted_ceiling()
    {
        var path = ExpertToJob.CostFloors.CostFloors.RosterQaConvergentPath;
        var baseline = ExpertToJob.CostFloors.CostFloors.BaselinePromptSizeCeilings[Agent];
        var cost = ExpertToJob.CostFloors.CostFloors.ConvergentRunCost(Agent, path);

        output.WriteLine($"{"Baseline Prompt Size",-24} {baseline,6} ×{path.Count + 1} = {baseline * (path.Count + 1),6}");
        for (var i = 0; i < path.Count; i++)
        {
            var size = ExpertToJob.CostFloors.CostFloors.ResultSize(path[i]);
            output.WriteLine($"{path[i],-24} {size,6} ×{path.Count - i} = {size * (path.Count - i),6}");
        }
        output.WriteLine($"{"convergent run",-24} {cost,20}   (target 8,000)");

        cost.Should().BeLessThanOrEqualTo(
            ExpertToJob.CostFloors.CostFloors.RosterQaConvergentRunCeiling,
            "the whole reference question is what a user is billed for, not one call of it");
    }

    [Fact]
    public void The_price_of_a_path_is_dominated_by_what_is_fetched_first()
    {
        // The property the ceiling above rests on, asserted rather than assumed: the same two
        // results cost more in the order that fetches the large one first. It is why the fix for a
        // cost regression is a smaller early payload or a shorter path — never a bigger cap.
        var early = ExpertToJob.CostFloors.CostFloors.ConvergentRunCost(
            Agent, ["employee_list", "roster_semantic_search"]);
        var late = ExpertToJob.CostFloors.CostFloors.ConvergentRunCost(
            Agent, ["roster_semantic_search", "employee_list"]);

        early.Should().BeGreaterThan(late);
    }

    [Fact]
    public void A_tool_with_neither_a_measured_ceiling_nor_a_pinned_estimate_cannot_be_priced()
    {
        // Silently pricing an unknown tool at zero would let a path grow for free.
        var act = () => ExpertToJob.CostFloors.CostFloors.ResultSize("employee_delete");

        act.Should().Throw<KeyNotFoundException>();
    }
}
