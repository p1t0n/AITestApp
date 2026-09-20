namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// Chat model wiring. The code is provider-agnostic (<c>IChatClient</c>); this binds the
/// default backend — the Gemini free tier via its OpenAI-compatible endpoint.
/// The token is read from <see cref="ApiKey"/> (config) or the <c>GEMINI_API_KEY</c> environment
/// variable; never commit a real token.
/// </summary>
public sealed class GeminiOptions
{
    public const string Section = "Ai:Gemini";

    /// <summary>The environment variable the credential is read from, ahead of <see cref="ApiKey"/>.
    /// Named once and shared by the construction branch that reads it and the Production guard that
    /// requires it (EXP-18), so a guard cannot come to demand a variable no branch reads.</summary>
    public const string ApiKeyVariable = "GEMINI_API_KEY";

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
    /// <see cref="Model"/>. Bound from <c>Ai:Gemini:Agents:&lt;agent&gt;</c>.</summary>
    public Dictionary<string, string> Agents { get; set; } = new();
}

/// <summary>
/// The second chat backend the seam can build: an Azure OpenAI deployment reached through the
/// plain OpenAI SDK against the resource's <c>/openai/v1/</c> endpoint
/// (<c>manuals/adr-chat-provider-seam.md</c> §2 decision 1). Declared here so the key shape is
/// settled before the seam is written; nothing consumes it yet.
///
/// <para><b>No base class shared with <see cref="GeminiOptions"/>, on purpose.</b> A base would
/// assert the two providers must stay shaped alike, which is the coupling the seam exists to
/// avoid — the Gemini block carries embedding and quota-breaker keys that mean nothing here, and
/// this one will grow an Entra credential that means nothing there.</para>
/// </summary>
public sealed class AzureFoundryOptions
{
    public const string Section = "Ai:AzureFoundry";

    /// <summary>The environment variable the credential is read from, ahead of <see cref="ApiKey"/>.
    /// Shared with the Production guard for the same reason as
    /// <see cref="GeminiOptions.ApiKeyVariable"/> (EXP-18).</summary>
    public const string ApiKeyVariable = "AZURE_FOUNDRY_API_KEY";

    /// <summary>The resource's OpenAI-compatible v1 endpoint, e.g.
    /// <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary><b>A deployment name, not a model id</b> — the spelling is shared with
    /// <see cref="GeminiOptions.Model"/>, the meaning is not. A deployment name is an
    /// operator-chosen string that is meaningless outside its own resource (ADR §2 decision 3).
    /// Documenting that is deliberately cheaper than forking the override dictionary into two
    /// shapes.</summary>
    public string Model { get; set; } = "";

    /// <summary>API key. Prefer the AZURE_FOUNDRY_API_KEY env var over config in real use.
    /// An Entra credential replaces this later (ADR §4); it is not a rejected option.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional per-agent deployment overrides, keyed by agent name. Bound from
    /// <c>Ai:AzureFoundry:Agents:&lt;agent&gt;</c> — per-agent overrides live inside the active
    /// provider's own block, never in a shared one (ADR §2 decision 10).</summary>
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

    /// <summary>Keycloak realm token authority, e.g. http://localhost:8080/realms/expert-to-job.</summary>
    public string Authority { get; set; } = "http://localhost:8080/realms/expert-to-job";

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
    /// <para>Since P1T-149 the identity carries this for real: the same set is
    /// <c>mcp:tool:&lt;name&gt;</c> scopes on the agent's Keycloak client, and the MCP server —
    /// not this key — is what narrows <c>tools/list</c> and refuses a call outside it. This stays
    /// as the local echo: it keeps the narrowing working against a stale realm import, and it is
    /// what the Baseline Prompt Size floor measures, which runs with no MCP server behind it.
    /// Both are asserted against <c>CostFloors.AgentToolAllowlists</c> so they cannot drift.
    /// See <c>manuals/mcp-tool-grants.md</c>.</para>
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
