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
/// <para>Embeddings are not a provider decision and never appear here — they keep calling Google
/// whatever chat does, which is why the discriminator is <c>Ai:Chat:Provider</c> and not
/// <c>Ai:Provider</c> (ADR §2 decision 3).</para>
/// </summary>
public enum ChatProvider
{
    /// <summary>The incumbent: the Gemini free tier through its OpenAI-compatible endpoint, with
    /// the two shims this repo carries for it. Bound from <see cref="GeminiOptions"/>.</summary>
    Gemini,

    /// <summary>An Azure OpenAI deployment reached through the plain OpenAI SDK against the
    /// resource's <c>/openai/v1/</c> endpoint. Bound from <see cref="AzureFoundryOptions"/>; its
    /// construction branch arrives in EXP-17.</summary>
    AzureFoundry,
}
