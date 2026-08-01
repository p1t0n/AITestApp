namespace CvManager.Application.Abstractions;

/// <summary>
/// Turns text into embedding vectors for semantic roster search. Provider-neutral on purpose:
/// returns plain <c>float[]</c> so the Application layer stays free of any embedding SDK or the
/// pgvector <c>Vector</c> type — callers convert at the persistence/query boundary.
/// </summary>
public interface IEmbedder
{
    /// <summary>The embedding model id in use, e.g. "text-embedding-3-small". Stamped onto chunks.</summary>
    string Model { get; }

    /// <summary>Embed a batch of inputs, preserving order. Empty input returns an empty batch.</summary>
    Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default);
}

/// <summary>A batch of embeddings plus the input tokens the provider billed (for cost logging).</summary>
public sealed record EmbeddingBatch(IReadOnlyList<float[]> Vectors, long InputTokens);
