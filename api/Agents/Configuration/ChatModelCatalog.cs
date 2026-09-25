namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// What the seam decided, in a form something other than the container can read (EXP-31): the
/// active provider, the default model, and the per-agent overrides — and <b>nothing else</b>.
///
/// <para>The credential and the endpoint deliberately do not appear on this type. It exists to be
/// served to a browser, and a catalog that carried them would be one <c>Results.Ok</c> away from
/// publishing them; keeping them out is structural rather than a rule someone has to remember at
/// the endpoint. Both live where they always did — inside the closure that builds the
/// <see cref="OpenAI.OpenAIClient"/>, which nothing downstream can look into.</para>
///
/// <para>Registered by <see cref="ChatProviderServiceCollectionExtensions.AddChatProvider"/> from
/// the same <c>ChatModels</c> value that registers the clients themselves, so the catalog cannot
/// come to disagree with what a call actually runs on.</para>
/// </summary>
/// <param name="Provider">The backend <c>Ai:Chat:Provider</c> named. Its <c>ToString()</c> is what
/// the API reports, matching the spelling the usage row already stores (EXP-19).</param>
/// <param name="Default">The model every agent without an override runs on. On
/// <see cref="ChatProvider.AzureFoundry"/> this is a deployment name, as everywhere else.</param>
/// <param name="Agents">Per-agent overrides, keyed by the agent key the agent resolves its client
/// by.</param>
public sealed record ChatModelCatalog(
    ChatProvider Provider,
    string Default,
    IReadOnlyDictionary<string, string> Agents)
{
    /// <summary>The model an agent key resolves to — the exact rule
    /// <see cref="ChatProviderServiceCollectionExtensions.ResolveAgentChatClient"/> applies, said
    /// in models rather than in clients: the keyed override if there is one, else the default.</summary>
    public string For(string agentKey) =>
        Agents.TryGetValue(agentKey, out var model) && !string.IsNullOrWhiteSpace(model)
            ? model
            : Default;
}
