namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// Embedding backend wiring for semantic roster search. Reuses the same OpenAI-compatible
/// <c>Gemini</c> config block as the chat client (endpoint + key), adding the embedding
/// model id. The token is read from <see cref="ApiKey"/> or the <c>GEMINI_API_KEY</c> env var;
/// never commit a real token.
///
/// <para>If the Gemini compat layer misbehaves for embeddings, point
/// <see cref="Endpoint"/> at OpenAI-direct/Azure — the chat client is unaffected.</para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string Section = "Gemini";

    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";

    /// <summary>Embedding model id.</summary>
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>Requested output dimensionality. gemini-embedding-001 defaults to 3072; the
    /// ExpertSearchChunk column is vector(1536), so the request must pin 1536.</summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>API key. Prefer the GEMINI_API_KEY env var over config in real use.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Seconds the embedder's quota breaker stays open after retries exhaust on 429s.
    /// While open every embed call fails fast with <c>EmbeddingQuotaExceededException</c> instead
    /// of spending more requests against a daily cap that cannot clear quickly (P1T-99).</summary>
    public int QuotaBreakerSeconds { get; set; } = 1800;
}
