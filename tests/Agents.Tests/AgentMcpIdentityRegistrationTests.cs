using ExpertToJob.Agents.Auth;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Mcp;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Tests the keyed multi-identity wiring: each agent registers its own MCP identity from a named
/// <c>McpAuth:&lt;agent&gt;</c> config section, and resolves its own keyed token provider + tool
/// source. Asserted through the DI container and a capturing Keycloak stand-in — no live servers.
/// </summary>
public class AgentMcpIdentityRegistrationTests
{
    private static IServiceProvider BuildProvider(
        CapturingHandler capture, params (string Key, string ClientId, string[] Tools)[] agents)
    {
        var settings = new Dictionary<string, string?>
        {
            ["McpServer:BaseUrl"] = "http://localhost:5100",
        };
        foreach (var (key, clientId, tools) in agents)
        {
            settings[$"McpAuth:{key}:Authority"] = "http://localhost:8080/realms/expert-to-job";
            settings[$"McpAuth:{key}:ClientId"] = clientId;
            settings[$"McpAuth:{key}:ClientSecret"] = $"{clientId}-secret";
            settings[$"McpAuth:{key}:Scope"] = "mcp:read";
            for (var i = 0; i < tools.Length; i++)
            {
                settings[$"McpAuth:{key}:Tools:{i}"] = tools[i];
            }
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(capture));
        services.AddOptions<McpServerOptions>().Bind(config.GetSection(McpServerOptions.Section));
        foreach (var (key, _, _) in agents)
        {
            services.AddAgentMcpIdentity(config, key);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Resolves_a_keyed_token_provider_that_authenticates_as_the_configured_client()
    {
        var capture = new CapturingHandler(accessToken: "tok-rqa");
        var sp = BuildProvider(capture, ("roster-qa", "agent-roster-qa", []));

        var provider = sp.GetRequiredKeyedService<IAccessTokenProvider>("roster-qa");
        var token = await provider.GetTokenAsync();

        token.Should().Be("tok-rqa");
        capture.Form["client_id"].Should().Be("agent-roster-qa");
        capture.Form["scope"].Should().Be("mcp:read");
    }

    [Fact]
    public async Task Two_agents_resolve_distinct_providers_each_with_its_own_client_identity()
    {
        var capture = new CapturingHandler(accessToken: "tok");
        var sp = BuildProvider(capture,
            ("roster-qa", "agent-roster-qa", []),
            ("cv-tailoring", "agent-cv-tailoring", []));

        var rosterQa = sp.GetRequiredKeyedService<IAccessTokenProvider>("roster-qa");
        var cvTailoring = sp.GetRequiredKeyedService<IAccessTokenProvider>("cv-tailoring");

        cvTailoring.Should().NotBeSameAs(rosterQa);

        await rosterQa.GetTokenAsync();
        await cvTailoring.GetTokenAsync();

        capture.Requests.Select(r => r["client_id"])
            .Should().BeEquivalentTo(["agent-roster-qa", "agent-cv-tailoring"]);
    }

    [Fact]
    public void Each_agent_resolves_its_own_keyed_tool_source()
    {
        var capture = new CapturingHandler(accessToken: "tok");
        var sp = BuildProvider(capture,
            ("roster-qa", "agent-roster-qa", []),
            ("cv-tailoring", "agent-cv-tailoring", []));

        var rosterQaTools = sp.GetRequiredKeyedService<IMcpToolSource>("roster-qa");
        var cvTailoringTools = sp.GetRequiredKeyedService<IMcpToolSource>("cv-tailoring");

        rosterQaTools.Should().NotBeNull();
        cvTailoringTools.Should().NotBeNull();
        cvTailoringTools.Should().NotBeSameAs(rosterQaTools);
    }

    [Fact]
    public void Each_agents_tool_source_carries_its_own_configured_tool_allowlist()
    {
        var capture = new CapturingHandler(accessToken: "tok");
        var sp = BuildProvider(capture,
            ("roster-qa", "agent-roster-qa", ["cv_get", "skill_list"]),
            ("cv-tailoring", "agent-cv-tailoring", ["style_exemplar_search"]));

        sp.GetRequiredKeyedService<IMcpToolSource>("roster-qa").Should().BeOfType<McpToolSource>()
            .Which.Allowlist.ToolNames.Should().BeEquivalentTo(["cv_get", "skill_list"]);
        sp.GetRequiredKeyedService<IMcpToolSource>("cv-tailoring").Should().BeOfType<McpToolSource>()
            .Which.Allowlist.ToolNames.Should().BeEquivalentTo(["style_exemplar_search"]);
    }

    [Fact]
    public void An_agent_with_no_configured_tools_keeps_the_whole_surface_its_token_carries()
    {
        var capture = new CapturingHandler(accessToken: "tok");
        var sp = BuildProvider(capture, ("roster-qa", "agent-roster-qa", []));

        sp.GetRequiredKeyedService<IMcpToolSource>("roster-qa").Should().BeOfType<McpToolSource>()
            .Which.Allowlist.ShowsEverything.Should().BeTrue();
    }
}
