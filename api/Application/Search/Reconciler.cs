namespace EmployeeManager.Application.Search;

/// <summary>
/// Pure diff between the chunks an employee <em>should</em> have (from <see cref="ChunkProjection"/>)
/// and the chunks currently persisted. Drives the reconciliation worker: content changes and new
/// sources become upserts (to re-embed), and sources that no longer exist become deletes.
///
/// <para>Chunks are matched by (SourceType, SourceId). A matched pair is an upsert only when the
/// content hash differs, so unchanged chunks cost nothing.</para>
/// </summary>
public static class Reconciler
{
    public static ChunkDiff Diff(IReadOnlyList<DesiredChunk> desired, IReadOnlyList<ExistingChunk> existing)
    {
        var existingByKey = existing.ToDictionary(e => (e.SourceType, e.SourceId));

        var upserts = new List<ChunkUpsert>();
        foreach (var chunk in desired)
        {
            if (existingByKey.TryGetValue((chunk.SourceType, chunk.SourceId), out var match))
            {
                // Re-embed only when the rendered content actually changed.
                if (match.ContentHash != chunk.ContentHash)
                {
                    upserts.Add(new ChunkUpsert(chunk, match.Id));
                }
            }
            else
            {
                upserts.Add(new ChunkUpsert(chunk, ExistingId: null));
            }
        }

        var desiredKeys = desired.Select(d => (d.SourceType, d.SourceId)).ToHashSet();
        var deletes = existing
            .Where(e => !desiredKeys.Contains((e.SourceType, e.SourceId)))
            .Select(e => e.Id)
            .ToList();

        return new ChunkDiff(upserts, deletes);
    }
}
