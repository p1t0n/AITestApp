namespace CvManager.Agents.Configuration;

/// <summary>
/// Chat model wiring. The code is provider-agnostic (<c>IChatClient</c>); this binds the
/// default backend — the Gemini free tier via its OpenAI-compatible endpoint.
/// The token is read from <see cref="ApiKey"/> (config) or the <c>GEMINI_API_KEY</c> environment
/// variable; never commit a real token.
/// </summary>
public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";

    /// <summary>Default model id. Pinned to an explicit generation: free-tier quotas differ
    /// per model row (3.5-flash-lite: RPD 500 vs every Flash-proper row: RPD 20), and a
    /// <c>-latest</c> alias may silently drift onto a low-quota row (P1T-114/P1T-115).
    /// Used by any agent without a per-agent override in <see cref="Agents"/>.</summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Gemini API key. Prefer the GEMINI_API_KEY env var over config in real use.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional per-agent model overrides, keyed by agent name (e.g. <c>cv-tailoring</c>).
    /// An agent listed here gets its own chat client on the named model; everyone else uses
    /// <see cref="Model"/>. Bound from <c>Gemini:Agents:&lt;agent&gt;</c>.</summary>
    public Dictionary<string, string> Agents { get; set; } = new();
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

    /// <summary>
    /// The agent's <b>Tool Allowlist</b> (P1T-146): the subset of the tools its scope carries that
    /// it is actually shown. Empty or absent means "everything the token carries" — narrowing is
    /// always an explicit act, never a silent side effect of a missing key.
    ///
    /// <para>This sits on the identity because that is where it belongs: a scope gates read
    /// against write, and this gates <i>which</i> read tools. Enforcing it in the client is the
    /// stand-in — P1T-149 moves it onto the agent's Keycloak identity and the MCP host.</para>
    /// </summary>
    public string[] Tools { get; set; } = [];
}

/// <summary>Where the MCP server lives and which resource (audience) tokens are minted for.</summary>
public sealed class McpServerOptions
{
    public const string Section = "McpServer";

    /// <summary>MCP server base URL (Streamable HTTP root). Default = local Mcp launch profile.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5100";
}
