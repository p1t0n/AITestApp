using ExpertToJob.Application.Abstractions;

namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// The embedder a host gets when it has no API key for its active embeddings provider (EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 6). It sends nothing and holds no
/// client; every call throws <see cref="EmbeddingCredentialMissingException"/>, which the query
/// paths already degrade on — so semantic search falls back to keyword matching instead of every
/// request failing.
///
/// <para><b>Why a stand-in rather than no registration at all.</b> Leaving <see cref="IEmbedder"/>
/// unregistered would make the three search services unresolvable, and the failure would surface as
/// a container error on the first request rather than as a named condition anyone can read. Leaving
/// the real embedder registered is worse: <see cref="System.ClientModel.ApiKeyCredential"/> rejects
/// an empty key, so the throw would come out of the DI factory wearing an SDK's words.</para>
///
/// <para><b>It still reports the configured model and tag.</b> Coverage, the reconciler's
/// stale-tag test and the search paths' tag filter all read <see cref="Tag"/>, and a keyless
/// development host has not changed which model its deployment is configured for — reporting
/// something else here would make those three answer a different question than the one the
/// deployment asks.</para>
/// </summary>
public sealed class CredentiallessEmbedder : IEmbedder
{
    private readonly EmbeddingsProvider _provider;

    public CredentiallessEmbedder(EmbeddingsProvider provider, string model)
    {
        _provider = provider;
        Model = model;
        Tag = OpenAICompatibleEmbedder.TagFor(provider, model);
    }

    public string Model { get; }

    public string Tag { get; }

    public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        => inputs.Count == 0
            ? Task.FromResult(new EmbeddingBatch([], 0))
            : throw new EmbeddingCredentialMissingException(
                $"No API key for the configured embeddings provider. "
                + $"'{EmbeddingServiceCollectionExtensions.ProviderKey}' is '{_provider}', so set "
                + $"{EmbeddingOptions.ApiKeyVariableFor(_provider)} (or "
                + $"'{EmbeddingOptions.SectionFor(_provider)}:ApiKey') to embed. Semantic search is "
                + "falling back to keyword matching. "
                + "See manuals/adr-embeddings-provider-seam.md.");
}
