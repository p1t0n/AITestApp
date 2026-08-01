using CvManager.Domain.Enums;
using Pgvector;

namespace CvManager.Infrastructure.Persistence;

/// <summary>
/// A derived read-model row for semantic roster search (RAG): one embeddable chunk of an
/// employee's free-text career narrative — a single work Experience, the employee's professional
/// Summary, or one Achievement bullet. This is not domain state: it is rebuilt from the aggregates by the
/// reconciliation worker, so it can be truncated and regenerated at any time. It lives in
/// Infrastructure (not Domain) because it carries a Postgres-specific <see cref="Vector"/> type and
/// is purely a persistence/retrieval concern.
///
/// <para><see cref="Embedding"/> is null until the worker embeds the row. Staleness is detected by
/// comparing <see cref="ContentHash"/> against a freshly rendered chunk (see the reconciler).</para>
/// </summary>
public class EmployeeSearchChunk
{
    public Guid Id { get; set; }

    /// <summary>Owning employee; chunks cascade-delete with the employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>What this chunk was rendered from.</summary>
    public SearchChunkSource SourceType { get; set; }

    /// <summary>
    /// Id of the source row: the Experience id, the Achievement id, or the Employee id for a
    /// <see cref="SearchChunkSource.Summary"/> chunk. Unique together with <see cref="SourceType"/>.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>The exact rendered text that was (or will be) embedded.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of <see cref="Content"/>; drives dirty detection.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>1536-dim embedding of <see cref="Content"/>; null until embedded.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>Embedding model id used, e.g. "text-embedding-3-small". Empty until embedded.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>When <see cref="Embedding"/> was written; null until embedded.</summary>
    public DateTimeOffset? EmbeddedAt { get; set; }
}
