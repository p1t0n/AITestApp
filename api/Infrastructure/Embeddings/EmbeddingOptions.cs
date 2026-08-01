namespace CvManager.Infrastructure.Embeddings;

/// <summary>
/// Embedding backend wiring for semantic roster search. Reuses the same OpenAI-compatible
/// <c>GitHubModels</c> config block as the chat client (endpoint + PAT), adding the embedding
/// model id. The token is read from <see cref="ApiKey"/> or the <c>GITHUB_TOKEN</c> env var;
/// never commit a real token.
///
/// <para>If GitHub Models does not serve an embedding deployment on the PAT, point
/// <see cref="Endpoint"/> at OpenAI-direct/Azure — the chat client is unaffected.</para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string Section = "GitHubModels";

    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "https://models.github.ai/inference";

    /// <summary>Embedding model id (1536-dim). Matches the vector(1536) column.</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>API key / PAT. Prefer the GITHUB_TOKEN env var over config in real use.</summary>
    public string ApiKey { get; set; } = "";
}
