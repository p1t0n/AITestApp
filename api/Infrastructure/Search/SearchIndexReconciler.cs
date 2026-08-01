using CvManager.Application.Abstractions;
using CvManager.Application.Search;
using CvManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace CvManager.Infrastructure.Search;

/// <summary>One reconciliation pass: sync the chunk table to the roster, then embed what's stale.</summary>
public interface ISearchIndexReconciler
{
    Task<ReconcileReport> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>Counts from a single pass, for logging and test assertions.</summary>
public sealed record ReconcileReport(int Inserted, int Updated, int Deleted, int Embedded, long EmbeddingTokens)
{
    public static readonly ReconcileReport Empty = new(0, 0, 0, 0, 0);
    public bool DidWork => Inserted + Updated + Deleted + Embedded > 0;
}

/// <summary>
/// Rebuilds <see cref="EmployeeSearchChunk"/> rows from the roster and embeds the stale ones.
///
/// <para>Two phases per pass: (1) project every employee to its desired chunks and diff against the
/// persisted chunks (<see cref="Reconciler"/>), applying inserts (embedding cleared), content
/// updates (embedding cleared), and orphan deletes; (2) embed every chunk whose embedding is null,
/// in batches. Because a fresh/edited chunk has a null embedding, the same loop backfills a cold
/// index and keeps a warm one current.</para>
/// </summary>
public sealed class SearchIndexReconciler : ISearchIndexReconciler
{
    private readonly AppDbContext _db;
    private readonly IEmbedder _embedder;
    private readonly SearchIndexOptions _options;
    private readonly ILogger<SearchIndexReconciler> _logger;

    public SearchIndexReconciler(
        AppDbContext db,
        IEmbedder embedder,
        IOptions<SearchIndexOptions> options,
        ILogger<SearchIndexReconciler> logger)
    {
        _db = db;
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReconcileReport> RunOnceAsync(CancellationToken ct = default)
    {
        var (inserted, updated, deleted) = await SyncChunksAsync(ct);
        var (embedded, tokens) = await EmbedPendingAsync(ct);

        var report = new ReconcileReport(inserted, updated, deleted, embedded, tokens);
        if (report.DidWork)
        {
            _logger.LogInformation(
                "Search index reconciled: +{Inserted} ~{Updated} -{Deleted} chunks, {Embedded} embedded ({Tokens} tokens)",
                inserted, updated, deleted, embedded, tokens);
        }

        return report;
    }

    private async Task<(int Inserted, int Updated, int Deleted)> SyncChunksAsync(CancellationToken ct)
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Include(e => e.Experiences)
            .ThenInclude(x => x.Achievements)
            .ToListAsync(ct);

        var desired = employees.SelectMany(ChunkProjection.Project).ToList();

        // Tracked so updates/deletes flush without a second lookup.
        var existingEntities = await _db.EmployeeSearchChunks.ToListAsync(ct);
        var existingById = existingEntities.ToDictionary(e => e.Id);
        var existing = existingEntities
            .Select(e => new ExistingChunk(e.Id, e.SourceType, e.SourceId, e.ContentHash))
            .ToList();

        var diff = Reconciler.Diff(desired, existing);
        if (diff.IsEmpty)
        {
            return (0, 0, 0);
        }

        var inserted = 0;
        var updated = 0;
        foreach (var upsert in diff.Upserts)
        {
            if (upsert.ExistingId is { } id && existingById.TryGetValue(id, out var row))
            {
                // Content changed: refresh it and clear the embedding so phase 2 re-embeds.
                row.Content = upsert.Chunk.Content;
                row.ContentHash = upsert.Chunk.ContentHash;
                row.Embedding = null;
                row.Model = string.Empty;
                row.EmbeddedAt = null;
                updated++;
            }
            else
            {
                _db.EmployeeSearchChunks.Add(new EmployeeSearchChunk
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = upsert.Chunk.EmployeeId,
                    SourceType = upsert.Chunk.SourceType,
                    SourceId = upsert.Chunk.SourceId,
                    Content = upsert.Chunk.Content,
                    ContentHash = upsert.Chunk.ContentHash,
                    Embedding = null,
                });
                inserted++;
            }
        }

        foreach (var deleteId in diff.Deletes)
        {
            if (existingById.TryGetValue(deleteId, out var row))
            {
                _db.EmployeeSearchChunks.Remove(row);
            }
        }

        await _db.SaveChangesAsync(ct);
        return (inserted, updated, diff.Deletes.Count);
    }

    private async Task<(int Embedded, long Tokens)> EmbedPendingAsync(CancellationToken ct)
    {
        var pending = await _db.EmployeeSearchChunks
            .Where(c => c.Embedding == null)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return (0, 0);
        }

        var embedded = 0;
        long tokens = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var batch in Chunk(pending, Math.Max(1, _options.EmbedBatchSize)))
        {
            var result = await _embedder.EmbedAsync(batch.Select(c => c.Content).ToList(), ct);
            tokens += result.InputTokens;

            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Embedding = new Vector(result.Vectors[i]);
                batch[i].Model = _embedder.Model;
                batch[i].EmbeddedAt = now;
                embedded++;
            }

            await _db.SaveChangesAsync(ct);
        }

        return (embedded, tokens);
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }
}
