using EmployeeManager.Agents.Agents;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Boots the real application host and resolves the registered agent, exercising the production
/// DI composition in <c>Program.cs</c> — including the keyed <c>IMcpToolSource</c> resolution.
/// No network: agents and the chat/tool clients construct lazily and only reach out on a request.
/// </summary>
public class AgentsHostCompositionTests
{
    [Fact]
    public void Resolves_the_roster_qa_agent_with_its_keyed_mcp_identity()
    {
        using var factory = new WebApplicationFactory<Program>();

        var agent = factory.Services.GetRequiredService<IChatAgent>();

        agent.Name.Should().Be("roster-qa");
    }
}
