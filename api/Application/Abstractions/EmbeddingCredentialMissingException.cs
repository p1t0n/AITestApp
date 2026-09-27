namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// This host has no API key for the embeddings provider it is configured to use, so nothing was
/// sent anywhere. <b>A development condition, and only ever one</b>: a Production host with no key
/// refuses to start (<c>EmbeddingProviderStartupGuard</c>), so reaching this at runtime means a
/// developer is running the stack without a credential — where semantic search degrading to keyword
/// matching is the behaviour worth having, and an unreadable SDK error on every query is not.
/// </summary>
public sealed class EmbeddingCredentialMissingException : EmbeddingUnavailableException
{
    public EmbeddingCredentialMissingException(string message)
        : base(message)
    {
    }
}
