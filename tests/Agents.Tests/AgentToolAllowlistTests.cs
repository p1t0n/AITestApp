using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Mcp;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The Tool Allowlist (P1T-146): each agent identity is shown only the tools it uses, so an unused
/// schema is not re-sent on every iteration and the model has fewer wrong tools to pick.
///
/// <para>Two halves, and both matter. The filter itself is a pure function over a tool list. The
/// shipped <c>appsettings.json</c> is what the agents actually run on — and it is also what the
/// Baseline Prompt Size floor measures against, via
/// <see cref="ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists"/>. If config and that
/// declaration drift, the committed cost ceilings stop describing the running system, so the drift
/// is what this asserts.</para>
/// </summary>
public class AgentToolAllowlistTests
{
    private static AITool Tool(string name) => AIFunctionFactory.Create(() => "{}", name);

    private static readonly AITool[] ReadSurface =
        ExpertToJob.CostFloors.CostFloors.ReadScopeTools.Order().Select(Tool).ToArray();

    // ---- the filter ----

    [Fact]
    public void Narrows_the_advertised_surface_to_the_configured_tools()
    {
        var allowlist = new AgentToolAllowlist(["cv_get", "skill_list"]);

        allowlist.Apply(ReadSurface).Select(t => t.Name)
            .Should().BeEquivalentTo(["cv_get", "skill_list"]);
    }

    [Fact]
    public void An_absent_list_shows_everything_the_token_carries()
    {
        // Narrowing must be an explicit act: a missing config key crippling an agent is exactly
        // the silent failure this feature would otherwise introduce.
        var allowlist = new AgentToolAllowlist([]);

        allowlist.ShowsEverything.Should().BeTrue();
        allowlist.Apply(ReadSurface).Should().BeSameAs(ReadSurface);
    }

    [Fact]
    public void Preserves_the_order_the_server_advertised()
    {
        var offered = new[] { Tool("cv_get"), Tool("skill_list"), Tool("expert_list") };

        new AgentToolAllowlist(["expert_list", "cv_get"]).Apply(offered).Select(t => t.Name)
            .Should().Equal("cv_get", "expert_list");
    }

    [Fact]
    public void Reports_allowlisted_tools_the_server_never_advertised()
    {
        // A typo, or a scope that no longer carries the tool. Either way the surface is narrower
        // than configured, and McpToolSource logs it rather than shipping the agent quietly short.
        var allowlist = new AgentToolAllowlist(["cv_get", "cv_gte", "expert_creat"]);

        allowlist.MissingFrom(ReadSurface).Should().Equal("cv_gte", "expert_creat");
    }

    [Fact]
    public void Reports_nothing_missing_when_it_shows_everything()
    {
        AgentToolAllowlist.All.MissingFrom(ReadSurface).Should().BeEmpty();
    }

    // ---- the shipped configuration ----

    public static TheoryData<string> AgentKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists.Keys.Order())
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AgentKeys))]
    public void Each_agent_is_configured_with_exactly_its_declared_tool_set(string agentKey)
    {
        using var factory = new WebApplicationFactory<Program>();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<McpClientAuthOptions>>().Get(agentKey);

        options.Tools.Should().BeEquivalentTo(
            ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists[agentKey],
            $"the Baseline Prompt Size ceiling for {agentKey} is measured against this set");
    }

    [Theory]
    [MemberData(nameof(AgentKeys))]
    public void No_agent_allowlists_a_tool_its_scope_cannot_carry(string agentKey)
    {
        using var factory = new WebApplicationFactory<Program>();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<McpClientAuthOptions>>().Get(agentKey);
        var carried = options.Scope.Contains("mcp:write")
            ? ExpertToJob.CostFloors.CostFloors.WriteScopeTools
            : ExpertToJob.CostFloors.CostFloors.ReadScopeTools;

        // Capability is enforced by the token, so the allowlist can only ever narrow what the
        // scope already grants. A name outside it would be a tool the agent never receives —
        // a dead entry that reads as capability it does not have.
        options.Tools.Should().BeSubsetOf(carried);
    }

    [Fact]
    public void Every_registered_agent_identity_has_an_allowlist()
    {
        // No agent may quietly keep the whole surface: the empty default exists so a narrowing is
        // deliberate, not so an agent can be forgotten.
        using var factory = new WebApplicationFactory<Program>();
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<McpClientAuthOptions>>();

        using var _ = new AssertionScope();
        foreach (var key in ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists.Keys)
        {
            monitor.Get(key).Tools.Should().NotBeEmpty($"{key} is a registered MCP identity");
        }
    }

    [Fact]
    public void Roster_qa_is_shown_four_of_the_eleven_read_tools()
    {
        // The headline of P1T-146, spelled out: 26% of a 160,220-token run was seven tool schemas
        // the traced run never called, re-sent on all ten iterations.
        using var factory = new WebApplicationFactory<Program>();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<McpClientAuthOptions>>().Get("roster-qa");

        options.Tools.Should().BeEquivalentTo(
            ["roster_semantic_search", "skill_list", "expert_list", "cv_get"]);
        ExpertToJob.CostFloors.CostFloors.ReadScopeTools.Should().HaveCount(11);
    }
}
