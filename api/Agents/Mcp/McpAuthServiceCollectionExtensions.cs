using EmployeeManager.Agents.Auth;
using EmployeeManager.Agents.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmployeeManager.Agents.Mcp;

/// <summary>
/// Registers a per-agent MCP identity: its own client-credentials token provider and MCP tool
/// source, both keyed by the agent name, bound to the agent's <c>McpAuth:&lt;agent&gt;</c> config
/// section. Each agent thus authenticates to the MCP server as its own Keycloak client with its
/// own scope, without sharing a single global identity.
/// </summary>
public static class McpAuthServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMcpIdentity(
        this IServiceCollection services, IConfiguration config, string agentKey)
    {
        services.AddOptions<McpClientAuthOptions>(agentKey)
            .Bind(config.GetSection($"{McpClientAuthOptions.Section}:{agentKey}"));

        services.AddKeyedSingleton<IAccessTokenProvider>(agentKey, (sp, key) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<McpClientAuthOptions>>().Get((string)key!);
            return new ClientCredentialsTokenProvider(
                sp.GetRequiredService<IHttpClientFactory>(), options, sp.GetRequiredService<TimeProvider>());
        });

        services.AddKeyedSingleton<IMcpToolSource>(agentKey, (sp, key) =>
            new McpToolSource(
                sp.GetRequiredService<IOptions<McpServerOptions>>(),
                sp.GetRequiredKeyedService<IAccessTokenProvider>(key),
                sp.GetRequiredService<ILoggerFactory>()));

        return services;
    }
}
