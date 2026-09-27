using System.ClientModel;
using ExpertToJob.Application.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// <b>The embeddings seam</b> (EXP-64, <c>manuals/adr-embeddings-provider-seam.md</c> §2): the one
/// place in the repo where an embeddings provider is chosen. Opt-in (not part of
/// <c>AddInfrastructure</c>): only the MCP service, which runs the reconciliation worker and the
/// semantic search query, calls this — the Web API has no need to embed.
///
/// <para>Shaped like the chat seam deliberately. Everything provider-specific happens in the
/// <b>construction branch</b> — the few lines that build the <see cref="OpenAIClient"/> and its
/// embedding generator. Everything after it is provider-neutral: it holds an
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> and cannot tell who is behind it, which is
/// why <see cref="OpenAICompatibleEmbedder"/> needed renaming rather than forking.</para>
///
/// <para>Only the Gemini branch exists today. Azure OpenAI is named by
/// <see cref="EmbeddingsProvider"/> and arrives with EXP-67; asking for it now fails at startup
/// saying so.</para>
/// </summary>
public static class EmbeddingServiceCollectionExtensions
{
    /// <summary>The discriminator. Separate from <c>Ai:Chat:Provider</c>, because the two backends
    /// move independently (ADR §4).</summary>
    public const string ProviderKey = "Ai:Embeddings:Provider";

    /// <summary>The global similarity floor these settings replaced. Named once, here, because the
    /// guard below is the only thing left in the repo that may spell it.</summary>
    public const string LegacyMinSimilarityKey = "SemanticSearch:MinSimilarity";

    public static IServiceCollection AddEmbeddingProvider(
        this IServiceCollection services, IConfiguration config)
    {
        var (provider, cfg) = ResolveProvider(config);

        // The one fact downstream code is allowed to know about the choice: its name. Registered as
        // the enum rather than a string so a reader still fails at the config edge.
        services.AddSingleton(typeof(EmbeddingsProvider), provider);

        // ---- the construction branch: the only code below that knows a provider's name ----
        switch (provider)
        {
            case EmbeddingsProvider.Gemini:
                AddGeminiEmbeddingClient(services, provider, cfg);
                break;
            default:
                // Unreachable while Defaults() is the only source of an options object: it throws
                // for a provider with no branch. Kept so that adding a Defaults entry without a
                // branch fails here rather than registering an embedder over nothing.
                throw new InvalidOperationException(
                    $"'{ProviderKey}' bound to {provider}, which no construction branch builds. The "
                    + "Azure OpenAI branch arrives with EXP-67. "
                    + "See manuals/adr-embeddings-provider-seam.md.");
        }

        // ---- provider-neutral from here down ----
        services.AddSingleton<IEmbedder>(sp => new OpenAICompatibleEmbedder(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            cfg.EmbeddingModel,
            cfg.Dimensions,
            sp.GetRequiredService<ILogger<OpenAICompatibleEmbedder>>(),
            clock: sp.GetService<TimeProvider>() ?? TimeProvider.System,
            quotaBreakerWindow: TimeSpan.FromSeconds(Math.Max(1, cfg.QuotaBreakerSeconds))));

        return services;
    }

    /// <summary>
    /// The active provider and its bound settings — the one read of the discriminator, shared by
    /// this seam and by <c>AddSearchIndexing</c>, which needs the same answer for the similarity
    /// floor. A second parse would be a second thing to keep in agreement with this one.
    ///
    /// <para>Both startup throws live here, so neither depends on which caller ran first.</para>
    /// </summary>
    public static (EmbeddingsProvider Provider, EmbeddingOptions Options) ResolveProvider(
        IConfiguration config)
    {
        ThrowIfLegacyMinSimilarity(config);

        var provider = ReadProvider(config);
        var options = EmbeddingOptions.Defaults(provider);
        config.GetSection(EmbeddingOptions.SectionFor(provider)).Bind(options);
        return (provider, options);
    }

    /// <summary>
    /// Reads the discriminator. An unknown name throws here, at startup, in <b>every</b>
    /// environment — a typo'd provider is wrong everywhere, unlike a missing credential, which
    /// stays a normal development condition.
    ///
    /// <para>The value has to <b>name a member</b>, which is stricter than parsing as one:
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts a bare number, and
    /// accepts a comma-separated list as a flags combination even without
    /// <see cref="FlagsAttribute"/> — <c>"Gemini,AzureFoundry"</c> ORs to 1 and arrives as a
    /// perfectly defined <c>AzureFoundry</c>. Two providers asked for and one silently chosen is
    /// the failure this guard exists to prevent.</para>
    ///
    /// <para>An absent key means the incumbent, so a configuration that never mentions embeddings
    /// keeps its old behaviour and a switch is always written down on purpose (ADR §2
    /// decision 1).</para>
    /// </summary>
    public static EmbeddingsProvider ReadProvider(IConfiguration config)
    {
        var configured = config[ProviderKey];
        if (configured is null)
        {
            return EmbeddingsProvider.Gemini;
        }

        var named = Enum.GetNames<EmbeddingsProvider>().FirstOrDefault(
            name => string.Equals(name, configured.Trim(), StringComparison.OrdinalIgnoreCase));

        return named is null
            ? throw new InvalidOperationException(
                $"'{ProviderKey}' is '{configured}', which is not an embeddings provider this build "
                + $"knows. Valid values: {string.Join(", ", Enum.GetNames<EmbeddingsProvider>())}. "
                + "See manuals/adr-embeddings-provider-seam.md.")
            : Enum.Parse<EmbeddingsProvider>(named);
    }

    /// <summary>
    /// The credential for one provider, from that provider's own environment variable first and its
    /// own config path second — and from <b>nowhere else</b>. The env-var read is by name on
    /// purpose rather than a bound configuration path: a credential is a name read deliberately.
    ///
    /// <para>The reader is a parameter because it is the one input a test cannot supply honestly
    /// otherwise: process variables are global, so "no key set" would mean something different on a
    /// machine with <c>GEMINI_API_KEY</c> exported, and pinning one would mutate state a test
    /// running beside this one is reading.</para>
    /// </summary>
    public static string ResolveApiKey(
        EmbeddingsProvider provider, EmbeddingOptions cfg, Func<string, string?> readEnvironmentVariable)
        => readEnvironmentVariable(EmbeddingOptions.ApiKeyVariableFor(provider)) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : cfg.ApiKey;

    /// <summary>
    /// The Gemini construction branch: one OpenAI-compatible embedding client (endpoint +
    /// credential). No shim — neither of the two the chat client carries has an analog on the
    /// embeddings endpoint, which only ever returns vectors.
    /// </summary>
    private static void AddGeminiEmbeddingClient(
        IServiceCollection services, EmbeddingsProvider provider, EmbeddingOptions cfg)
    {
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(ResolveApiKey(provider, cfg, Environment.GetEnvironmentVariable)),
                new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) });
            return client.GetEmbeddingClient(cfg.EmbeddingModel).AsIEmbeddingGenerator();
        });
    }

    /// <summary>
    /// A leftover global floor fails loudly rather than binding to a property nobody reads any
    /// more. There is no deprecation window, for the same reason the chat seam has none: nothing
    /// outside this repo consumes this configuration, and a silently-ignored key is the exact
    /// failure the move was worth making to prevent — the host would start, the floor would fall
    /// back to a default nobody chose, and the first symptom would be search quietly returning the
    /// wrong rows.
    /// </summary>
    private static void ThrowIfLegacyMinSimilarity(IConfiguration config)
    {
        if (config[LegacyMinSimilarityKey] is null)
        {
            return;
        }

        var perProvider = string.Join(", ", Enum.GetValues<EmbeddingsProvider>()
            .Select(p => $"'{EmbeddingOptions.SectionFor(p)}:MinSimilarity'"));

        throw new InvalidOperationException(
            $"Configuration still carries '{LegacyMinSimilarityKey}'. The similarity floor is "
            + $"calibrated per embedding model and moved into each provider's own block: {perProvider}. "
            + "See manuals/adr-embeddings-provider-seam.md.");
    }
}
