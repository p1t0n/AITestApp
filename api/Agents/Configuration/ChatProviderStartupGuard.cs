using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// The Production credential guard (EXP-18, <c>manuals/adr-chat-provider-seam.md</c> §2 decision 6):
/// a Production host refuses to boot without a key for the provider <c>Ai:Chat:Provider</c> actually
/// names. Without it, an Azure-configured Production host with no Azure key started happily and
/// failed on the first agent call — a startup mistake discovered at request time.
///
/// <para><b>Two failure modes, kept apart on purpose.</b> An unknown provider name throws in every
/// environment, out of <see cref="ChatProviderServiceCollectionExtensions.ReadProvider"/> — a typo
/// is wrong everywhere. A <em>missing credential</em> throws here, in Production only: running the
/// agents without a key is a normal development condition, where a model call degrades rather than
/// taking the host down with it.</para>
///
/// <para>This lives beside the seam rather than inline in <c>api/Agents/Program.cs</c> so that both
/// halves of that rule are reachable by a test. It is not a second place a provider is chosen: it
/// asks the seam which one is active and then asks the seam's own
/// <see cref="ChatCredential.Resolve"/> whether that provider has a credential, so a guard cannot
/// come to demand a key no branch would use — nor to accept one the runtime would refuse
/// (EXP-97).</para>
///
/// <para>Embeddings are not covered here and must not become conditional on the chat provider: they
/// follow their own key and are registered in the MCP host, not this one. Their guard is
/// <c>EmbeddingProviderStartupGuard</c>, which asks the same question of
/// <c>Ai:Embeddings:Provider</c> — a Production host with an Azure chat key and no embeddings key
/// for its active embeddings provider is refused by that one, not by this one.</para>
/// </summary>
public static class ChatProviderStartupGuard
{
    /// <summary>
    /// Throws when a Production host has no credential for its active chat provider, in either
    /// place that provider's construction branch reads: the environment variable it reads by name,
    /// or the provider's own <c>ApiKey</c> config path.
    ///
    /// <para>It does not decide that for itself — it asks <see cref="ChatCredential.Resolve"/>, the
    /// same call the construction branch registers its credential from (EXP-97). It used to repeat
    /// the two lookups, and the copies disagreed about a whitespace-only environment variable:
    /// this guard read it with <see cref="string.IsNullOrWhiteSpace"/>, fell back to the config key
    /// and booted, while <see cref="ChatCredential.Resolve"/> took any non-empty value and resolved
    /// to missing. A Production host set up that way started and then returned the EXP-93 503 on
    /// every agent call — a startup mistake discovered at request time, which is the one thing this
    /// guard exists to prevent.</para>
    /// </summary>
    /// <param name="config">The host's configuration — the discriminator and the provider blocks.</param>
    /// <param name="environment">The host environment. Non-Production returns without a check.</param>
    /// <param name="readEnvironmentVariable">
    /// How to read an environment variable, defaulting to the process. A parameter because it is
    /// the one input a test cannot supply honestly otherwise: process variables are global, so
    /// "no key set" would otherwise mean something different on a machine with
    /// <c>GEMINI_API_KEY</c> exported, and pinning it would mutate state a test running beside this
    /// one is reading.
    /// </param>
    public static void RequireActiveProviderCredential(
        IConfiguration config,
        IHostEnvironment environment,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        var provider = ChatProviderServiceCollectionExtensions.ReadProvider(config);
        var credential = ChatCredential.Resolve(
            provider,
            ChatProviderServiceCollectionExtensions.Bind(config, provider),
            readEnvironmentVariable);

        if (!credential.IsMissing)
        {
            return;
        }

        // The two spellings an operator would set come off the runtime's own failure, so the
        // startup message and the request-time 503 can never name different settings.
        var missing = new ChatCredentialMissingException(provider);
        throw new InvalidOperationException(
            $"No API key for the configured chat provider. "
            + $"'{ChatProviderServiceCollectionExtensions.ProviderKey}' is '{provider}', so set "
            + $"{missing.Variable} (or '{missing.ConfigPath}') before running in Production. "
            + "See manuals/adr-chat-provider-seam.md.",
            missing);
    }
}
