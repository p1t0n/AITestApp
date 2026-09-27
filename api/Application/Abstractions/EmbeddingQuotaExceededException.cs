namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// The embedding provider's quota stayed exhausted after the embedder's own bounded retries —
/// typically the free tier's daily request cap, which no short retry can outwait. Callers that
/// schedule embedding work (the reconcile worker) should back off for a long window instead of
/// retrying on their normal cadence, or each pass burns more of the next day's quota (P1T-98).
///
/// <para>One of the two <see cref="EmbeddingUnavailableException"/> conditions a query path
/// degrades on, and the only one the reconciler's long backoff is for: a missing credential does
/// not clear by waiting.</para>
/// </summary>
public sealed class EmbeddingQuotaExceededException : EmbeddingUnavailableException
{
    public EmbeddingQuotaExceededException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
