namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// Turns text into embedding vectors for semantic roster search. Provider-neutral on purpose:
/// returns plain <c>float[]</c> so the Application layer stays free of any embedding SDK or the
/// pgvector <c>Vector</c> type — callers convert at the persistence/query boundary.
/// </summary>
public interface IEmbedder
{
    /// <summary>The embedding model id in use, e.g. "gemini-embedding-001". The bare name, without
    /// the provider: <see cref="Tag"/> is what gets stamped onto a vector.</summary>
    string Model { get; }

    /// <summary>
    /// <c>&lt;provider&gt;/&lt;model&gt;</c> — the identity stamped onto every vector this embedder
    /// produces, and the unit of "these two vectors are comparable"
    /// (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 9). The model alone would not do:
    /// on Azure the model id is a <b>deployment name</b> someone chooses, so two providers can
    /// honestly report the same one.
    ///
    /// <para>Defaults to the bare <see cref="Model"/>, which is what an embedder with no provider
    /// behind it — a test fake, or a vector written before tagging existed — truthfully is. That is
    /// also exactly the shape the reconciler's relabel step recognises as legacy. The real
    /// embedder overrides it; the construction branch is the only place that knows a provider's
    /// name, so it is the only place the tag can be formed.</para>
    /// </summary>
    string Tag => Model;

    /// <summary>Embed a batch of inputs, preserving order. Empty input returns an empty batch.</summary>
    Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default);
}

/// <summary>A batch of embeddings plus the input tokens the provider billed (for cost logging).</summary>
public sealed record EmbeddingBatch(IReadOnlyList<float[]> Vectors, long InputTokens);
