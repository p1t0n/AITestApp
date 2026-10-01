namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// One chat provider's settings. Each provider's chat keys live in that provider's own
/// configuration block — <see cref="SectionFor"/> is the one place that mapping is written, and it
/// is deliberately the same block the embeddings seam reads, because the endpoint and the
/// credential really are shared (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 2).
///
/// <para><b>One class with a per-provider <see cref="Defaults"/> lookup, not two classes and not a
/// base class</b> (EXP-89). The shape this replaces was two independent types whose stated reason
/// was that a <em>base</em> would assert the providers must stay shaped alike. That reason holds
/// against inheritance and not against this: the values that differ between providers are the
/// <see cref="Defaults"/> entries, which are free to disagree about every property —
/// <see cref="Endpoint"/> and <see cref="Model"/> already do — and a provider that grows a key the
/// other has no meaning for gets a property the other's defaults never set. It is the shape
/// <see cref="Infrastructure.Embeddings.EmbeddingOptions"/> has carried since EXP-64, where
/// <c>MinSimilarity</c> is calibrated per model and the two providers' numbers are an order apart.</para>
///
/// <para>The credential is never bound from here in practice: <see cref="ApiKey"/> is the fallback
/// path, and the provider's own environment variable (<see cref="ApiKeyVariableFor"/>) is read
/// first. Never commit a real token.</para>
/// </summary>
public sealed class ChatProviderOptions
{
    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The default model every agent runs on unless it has an entry in
    /// <see cref="Agents"/>. <b>A deployment name on Azure, a model id on Gemini</b> — the spelling
    /// is shared, the meaning is not. A deployment name is an operator-chosen string that is
    /// meaningless outside its own resource (ADR §2 decision 3); documenting that is deliberately
    /// cheaper than forking the override dictionary into two shapes.</summary>
    public string Model { get; set; } = "";

    /// <summary>API key. Prefer the provider's environment variable over config in real use.
    /// On Azure an Entra credential replaces this later (ADR §4); it is not a rejected option.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional per-agent <see cref="Model"/> overrides, keyed by agent name (e.g.
    /// <c>cv-tailoring</c>). An agent listed here gets its own chat client on the named model;
    /// everyone else uses <see cref="Model"/>. Bound from the active provider's own section —
    /// <c>Ai:Gemini:Agents:&lt;agent&gt;</c> or <c>Ai:AzureFoundry:Agents:&lt;agent&gt;</c> — because
    /// per-agent overrides live inside that block and never in a shared one (ADR §2 decision 10).</summary>
    public Dictionary<string, string> Agents { get; set; } = new();

    /// <summary>The configuration section a provider's chat settings are bound from.</summary>
    public static string SectionFor(ChatProvider provider) => provider switch
    {
        ChatProvider.Gemini => "Ai:Gemini",
        ChatProvider.AzureFoundry => "Ai:AzureFoundry",
        var unmapped => throw new InvalidOperationException(
            $"No configuration section is mapped for chat provider {unmapped}. A new "
            + $"{nameof(ChatProvider)} member needs an entry here."),
    };

    /// <summary>The environment variable a provider's credential is read from, <b>and only that
    /// provider's</b>. Named once and shared by the construction branch that reads it and the
    /// Production guard that requires it (EXP-18), so a guard cannot come to demand a variable no
    /// branch reads — and so a request aimed at Azure can never carry the Google key.</summary>
    public static string ApiKeyVariableFor(ChatProvider provider) => provider switch
    {
        ChatProvider.Gemini => "GEMINI_API_KEY",
        ChatProvider.AzureFoundry => "AZURE_FOUNDRY_API_KEY",
        var unmapped => throw new InvalidOperationException(
            $"No API key variable is mapped for chat provider {unmapped}. A new "
            + $"{nameof(ChatProvider)} member needs an entry here."),
    };

    /// <summary>A provider's code defaults, before its configuration block is bound over them. A
    /// member added without an entry fails here, rather than binding an empty block and chatting
    /// against nothing.</summary>
    public static ChatProviderOptions Defaults(ChatProvider provider) => provider switch
    {
        ChatProvider.Gemini => new ChatProviderOptions
        {
            Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai",
            // Pinned to an explicit generation: free-tier quotas differ per model row
            // (3.5-flash-lite: RPD 500 vs every Flash-proper row: RPD 20), and a `-latest` alias may
            // silently drift onto a low-quota row (P1T-114/P1T-115).
            Model = "gemini-3.5-flash-lite",
        },
        // Deliberately empty: the shipped `Ai:AzureFoundry` block carries the endpoint and the
        // deployment name, and a code default here would be the same two strings written twice.
        // An unconfigured Azure host fails on an empty endpoint rather than on a resource nobody
        // named.
        ChatProvider.AzureFoundry => new ChatProviderOptions(),
        var unbuilt => throw new InvalidOperationException(
            $"'{ChatProviderServiceCollectionExtensions.ProviderKey}' is '{unbuilt}', which has no "
            + $"code defaults. A new {nameof(ChatProvider)} member needs an entry here as well as a "
            + "construction branch. See manuals/adr-chat-provider-seam.md."),
    };
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
