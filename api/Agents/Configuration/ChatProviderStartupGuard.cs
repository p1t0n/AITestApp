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
/// asks the seam which one is active and looks up that provider's credential, and the variable
/// names it requires are the same constants the construction branches read
/// (<see cref="GeminiOptions.ApiKeyVariable"/>, <see cref="AzureFoundryOptions.ApiKeyVariable"/>),
/// so a guard cannot come to demand a key no branch would use.</para>
///
/// <para>Embeddings are not covered here and must not become conditional on the chat provider: they
/// call Google whatever chat does (ADR, "The decision"), and they are registered in the MCP host,
/// not this one.</para>
/// </summary>
public static class ChatProviderStartupGuard
{
    /// <summary>
    /// Throws when a Production host has no credential for its active chat provider, in either
    /// place that provider's construction branch reads: the environment variable it reads by name,
    /// or the provider's own <c>ApiKey</c> config path.
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
        var (variable, configPath) = CredentialFor(provider);
        var readVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        if (!string.IsNullOrWhiteSpace(readVariable(variable))
            || !string.IsNullOrWhiteSpace(config[configPath]))
        {
            return;
        }

        throw new InvalidOperationException(
            $"No API key for the configured chat provider. "
            + $"'{ChatProviderServiceCollectionExtensions.ProviderKey}' is '{provider}', so set "
            + $"{variable} (or '{configPath}') before running in Production. "
            + "See manuals/adr-chat-provider-seam.md.");
    }

    /// <summary>Where the active provider's credential comes from. A new
    /// <see cref="ChatProvider"/> member needs an entry here as well as a construction branch —
    /// the throw says so rather than letting a provider boot Production unguarded.</summary>
    private static (string Variable, string ConfigPath) CredentialFor(ChatProvider provider) =>
        provider switch
        {
            ChatProvider.Gemini =>
                (GeminiOptions.ApiKeyVariable, $"{GeminiOptions.Section}:ApiKey"),
            ChatProvider.AzureFoundry =>
                (AzureFoundryOptions.ApiKeyVariable, $"{AzureFoundryOptions.Section}:ApiKey"),
            var unguarded => throw new InvalidOperationException(
                $"'{ChatProviderServiceCollectionExtensions.ProviderKey}' bound to {unguarded}, "
                + $"whose credential this guard does not know. A new {nameof(ChatProvider)} member "
                + "needs an entry here."),
        };
}
