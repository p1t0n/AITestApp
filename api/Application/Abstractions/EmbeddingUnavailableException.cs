namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// The embeddings backend cannot serve this call, for a reason retrying inside the request will not
/// fix. The base of the two conditions a <em>query</em> path treats identically — it degrades to
/// lexical ranking over the same chunk pool rather than failing the caller — while the reconciler
/// still tells them apart, because only one of them earns the long backoff (EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decisions 6 and 7).
///
/// <para>Abstract on purpose: "unavailable" is a category, never a diagnosis. Every throw site
/// names which one it is, so a log line says whether a key is missing or a quota is spent.</para>
/// </summary>
public abstract class EmbeddingUnavailableException : Exception
{
    protected EmbeddingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
