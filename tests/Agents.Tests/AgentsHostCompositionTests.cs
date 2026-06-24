using EmployeeManager.Agents.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Boots the real application host and resolves the keyed MCP tool source registered in
/// <c>Program.cs</c>, exercising the production keyed-identity wiring. Resolving the tool source
/// (rather than the agent) keeps this offline and key-free: it constructs no chat client and
/// reaches out to nothing until a request is served.
/// </summary>
public class AgentsHostCompositionTests
{
    [Fact]
    public void Registers_the_roster_qa_agents_keyed_mcp_tool_source()
    {
        using var factory = new WebApplicationFactory<Program>();

        var toolSource = factory.Services.GetRequiredKeyedService<IMcpToolSource>("roster-qa");

        toolSource.Should().NotBeNull();
    }

    [Fact]
    public void Registers_the_cv_tailoring_agents_keyed_mcp_tool_source()
    {
        using var factory = new WebApplicationFactory<Program>();

        var toolSource = factory.Services.GetRequiredKeyedService<IMcpToolSource>("cv-tailoring");

        toolSource.Should().NotBeNull();
    }
}
