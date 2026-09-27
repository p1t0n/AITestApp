using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// The Production credential guard for embeddings (EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 6), mirroring
/// <c>ChatProviderStartupGuard</c> one seam over: a Production MCP host refuses to boot without a
/// key for the provider <c>Ai:Embeddings:Provider</c> actually names.
///
/// <para><b>Three failure modes, kept apart on purpose.</b> An unknown provider name throws in
/// every environment, out of <see cref="EmbeddingServiceCollectionExtensions.ReadProvider"/> — a
/// typo is wrong everywhere. A <em>missing credential in Production</em> throws here, at startup,
/// because a Production roster whose search silently stopped being semantic is a failure nobody
/// gets told about. The same missing credential in Development does not throw at all: the seam
/// registers <see cref="CredentiallessEmbedder"/> and semantic search degrades to keyword matching,
/// which is what running the stack without a key should feel like.</para>
///
/// <para>It is not a second place a provider is chosen: it asks the seam which one is active and
/// looks up that provider's credential through the same <see cref="EmbeddingServiceCollectionExtensions.ResolveApiKey"/>
/// the construction branch reads, so a guard cannot come to demand a key no branch would use.</para>
/// </summary>
public static class EmbeddingProviderStartupGuard
{
    /// <summary>
    /// Throws when a Production host has no credential for its active embeddings provider, in
    /// either place that provider's construction branch reads: the environment variable it reads by
    /// name, or the provider's own <c>ApiKey</c> config path.
    /// </summary>
    /// <param name="config">The host's configuration — the discriminator and the provider blocks.</param>
    /// <param name="environment">The host environment. Non-Production returns without a check.</param>
    /// <param name="readEnvironmentVariable">
    /// How to read an environment variable, defaulting to the process. A parameter because it is
    /// the one input a test cannot supply honestly otherwise: process variables are global, so
    /// "no key set" would mean something different on a machine with <c>GEMINI_API_KEY</c>
    /// exported, and pinning one would mutate state a test running beside this one is reading.
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

        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
        var readVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        if (!string.IsNullOrWhiteSpace(
                EmbeddingServiceCollectionExtensions.ResolveApiKey(provider, options, readVariable)))
        {
            return;
        }

        throw new InvalidOperationException(
            "No API key for the configured embeddings provider. "
            + $"'{EmbeddingServiceCollectionExtensions.ProviderKey}' is '{provider}', so set "
            + $"{EmbeddingOptions.ApiKeyVariableFor(provider)} (or "
            + $"'{EmbeddingOptions.SectionFor(provider)}:ApiKey') before running in Production. "
            + "See manuals/adr-embeddings-provider-seam.md.");
    }
}
