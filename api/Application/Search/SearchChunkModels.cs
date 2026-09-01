using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Search;

/// <summary>
/// A chunk the projection wants to exist for an expert, with its content already rendered and
/// hashed. Provider-neutral: no embedding vector, no EF type — the reconciliation worker embeds and
/// persists it. Addressed by (<see cref="SourceType"/>, <see cref="SourceId"/>).
/// </summary>
public sealed record DesiredChunk(
    Guid ExpertId,
    SearchChunkSource SourceType,
    Guid SourceId,
    string Content,
    string ContentHash);

/// <summary>
/// The minimal view of a persisted chunk the reconciler needs to diff: its row id (to update or
/// delete), its source key, and the hash of the content that was last embedded.
/// </summary>
public sealed record ExistingChunk(
    Guid Id,
    SearchChunkSource SourceType,
    Guid SourceId,
    string ContentHash);

/// <summary>
/// One chunk to (re)embed. <see cref="ExistingId"/> is null for an insert, or the row id to update
/// in place when the content of an existing chunk changed.
/// </summary>
public sealed record ChunkUpsert(DesiredChunk Chunk, Guid? ExistingId);

/// <summary>The reconciler's output: chunks to (re)embed, and stale chunk row ids to delete.</summary>
public sealed record ChunkDiff(IReadOnlyList<ChunkUpsert> Upserts, IReadOnlyList<Guid> Deletes)
{
    public bool IsEmpty => Upserts.Count == 0 && Deletes.Count == 0;
}
