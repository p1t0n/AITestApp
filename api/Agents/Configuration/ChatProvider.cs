namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// The backend a chat call goes to, named by <c>Ai:Chat:Provider</c>
/// (<c>manuals/adr-chat-provider-seam.md</c> §1). One provider is active per deployment: there is
/// no failover and no per-agent provider routing.
///
/// <para><b>An enum rather than a string, on purpose.</b> It is what makes an unknown value fail at
/// the configuration edge instead of somewhere further in — a typo'd provider name is wrong in
/// every environment (ADR §2 decision 6), and a string would have let one through to be compared
/// against and quietly miss. Nothing persists this type; the usage row stores
/// <c>provider.ToString()</c> (EXP-19).</para>
///
/// <para>Embeddings never appear here, but no longer because they are fixed: they are their own
/// provider decision, named by <c>Ai:Embeddings:Provider</c> and built by a separate seam in the MCP
/// host (<c>manuals/adr-embeddings-provider-seam.md</c>). That is still why the discriminator is
/// <c>Ai:Chat:Provider</c> and not <c>Ai:Provider</c> — one key would force the two to move together
/// and turn "switch chat back to Gemini to compare" into "and re-embed the whole index". The chat
/// ADR's §2 decision 3, which said embeddings keep calling Google whatever chat does, is superseded
/// by that ADR's §8.</para>
/// </summary>
public enum ChatProvider
{
    /// <summary>The incumbent: the Gemini free tier through its OpenAI-compatible endpoint, with
    /// the two shims this repo carries for it. Bound from <see cref="GeminiOptions"/>.</summary>
    Gemini,

    /// <summary>An Azure OpenAI deployment reached through the plain OpenAI SDK against the
    /// resource's <c>/openai/v1/</c> endpoint. Bound from <see cref="AzureFoundryOptions"/>, whose
    /// <c>Model</c> carries a <b>deployment name</b> rather than a model id, and built by a branch
    /// that deliberately attaches neither Gemini shim (EXP-17, ADR §3).</summary>
    AzureFoundry,
}
