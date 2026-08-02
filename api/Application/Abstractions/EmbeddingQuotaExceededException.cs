namespace CvManager.Application.Abstractions;

/// <summary>
/// The embedding provider's quota stayed exhausted after the embedder's own bounded retries —
/// typically the free tier's daily request cap, which no short retry can outwait. Callers that
/// schedule embedding work (the reconcile worker) should back off for a long window instead of
/// retrying on their normal cadence, or each pass burns more of the next day's quota (P1T-98).
/// </summary>
public sealed class EmbeddingQuotaExceededException : Exception
{
    public EmbeddingQuotaExceededException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
