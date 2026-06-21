namespace EmployeeManager.Agents.Configuration;

/// <summary>
/// Chat model wiring. The code is provider-agnostic (<c>IChatClient</c>); this binds the
/// default backend — GitHub Models, an OpenAI-compatible endpoint that is free with a PAT.
/// The token is read from <see cref="ApiKey"/> (config) or the <c>GITHUB_TOKEN</c> environment
/// variable; never commit a real token.
/// </summary>
public sealed class GitHubModelsOptions
{
    public const string Section = "GitHubModels";

    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "https://models.github.ai/inference";

    /// <summary>Model id (GitHub Models namespaces them, e.g. <c>openai/gpt-4o-mini</c>).</summary>
    public string Model { get; set; } = "openai/gpt-4o-mini";

    /// <summary>GitHub PAT. Prefer the GITHUB_TOKEN env var over config in real use.</summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// The Keycloak service-account (client-credentials) client this agent uses to obtain a
/// scoped JWT for the MCP server. Roster Q&amp;A carries <c>mcp:read</c> only — the MCP
/// server's scope filtering then hides every write/destructive tool from the agent.
/// </summary>
public sealed class McpClientAuthOptions
{
    public const string Section = "McpAuth";

    /// <summary>Keycloak realm token authority, e.g. http://localhost:8080/realms/cv-manager.</summary>
    public string Authority { get; set; } = "http://localhost:8080/realms/cv-manager";

    public string ClientId { get; set; } = "agent-roster-qa";

    /// <summary>Client secret. Prefer an env var / user-secrets over config in real use.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Space-delimited scopes requested for the token.</summary>
    public string Scope { get; set; } = "mcp:read";
}

/// <summary>Where the MCP server lives and which resource (audience) tokens are minted for.</summary>
public sealed class McpServerOptions
{
    public const string Section = "McpServer";

    /// <summary>MCP server base URL (Streamable HTTP root). Default = local Mcp launch profile.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5100";
}
