namespace CvManager.Agents.Handoff;

/// <summary>An agent's MCP identity as provenance: the OAuth client id it authenticates as and
/// the scopes it requests. Deliberately has no secret field — this type exists to travel inside
/// a <see cref="HandoffPackage"/>, where credentials must be unrepresentable.</summary>
public sealed record AgentIdentity(string ClientId, IReadOnlyList<string> Scopes);

/// <summary>Resolves an agent name to its MCP identity, or null for tool-less agents (they hold
/// no MCP identity at all — e.g. the JD extractor and the staffing narrative).</summary>
public interface IAgentIdentitySource
{
    AgentIdentity? Find(string agentName);
}

/// <summary>
/// Reads identities from the same <c>McpAuth:&lt;agent&gt;</c> config sections that register the
/// agents' token providers, touching only <c>ClientId</c> and <c>Scope</c> — the secret key is
/// never read, so it cannot leak into a package by construction.
/// </summary>
public sealed class ConfigAgentIdentitySource(IConfiguration config) : IAgentIdentitySource
{
    public AgentIdentity? Find(string agentName)
    {
        var section = config.GetSection($"McpAuth:{agentName}");
        var clientId = section["ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var scopes = (section["Scope"] ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new AgentIdentity(clientId, scopes);
    }
}
