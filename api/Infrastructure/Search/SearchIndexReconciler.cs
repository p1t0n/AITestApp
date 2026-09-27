using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace ExpertToJob.Infrastructure.Search;

/// <summary>One reconciliation pass: sync the chunk table to the roster, then embed what's stale.</summary>
public interface ISearchIndexReconciler
{
    Task<ReconcileReport> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>Counts from a single pass, for logging and test assertions.</summary>
/// <param name="Relabelled">Rows already embedded by the active model under its bare name, restamped
/// with the full <c>&lt;provider&gt;/&lt;model&gt;</c> tag. They are not re-embedded: the vectors are
/// the active model's, they were simply written before the tag existed (ADR §2 decision 12).</param>
/// <param name="Coverage">Chunks carrying the active tag with a vector, over all chunks, measured
/// after this pass. 1 when the index is fully on the current model; 1 when the index is empty,
/// because there is nothing left to rebuild.</param>
public sealed record ReconcileReport(
    int Inserted, int Updated, int Deleted, int Embedded, long EmbeddingTokens,
    int Relabelled = 0, double Coverage = 1.0)
{
    public static readonly ReconcileReport Empty = new(0, 0, 0, 0, 0);
    public bool DidWork => Inserted + Updated + Deleted + Embedded + Relabelled > 0;
}

/// <summary>
/// Rebuilds <see cref="ExpertSearchChunk"/> rows from the roster and embeds the stale ones.
///
/// <para>Two phases per pass: (1) project every expert to its desired chunks and diff against the
/// persisted chunks (<see cref="Reconciler"/>), applying inserts (embedding cleared), content
/// updates (embedding cleared), and orphan deletes; (2) embed every chunk whose vector is missing
/// <b>or was made by another model</b>, in batches. Because a fresh/edited chunk has a null
/// embedding, the same loop backfills a cold index and keeps a warm one current.</para>
///
/// <para><b>Tags and switches (EXP-65).</b> Every vector is stamped with the active embedder's
/// <see cref="IEmbedder.Tag"/>, and "wrong tag" is treated exactly like "no vector". That is why a
/// provider switch needs no migration and no one-off job: whatever the mismatch came from — a
/// changed <c>Ai:Embeddings:Provider</c>, a restored backup, the untagged leftovers of the
/// GitHub Models → Gemini switch (P1T-88) — this loop heals it (ADR §6). Two rules keep that from
/// costing more than it must: a row whose bare model name is already the active model is
/// <b>relabelled</b> rather than re-embedded, and every stale vector is <b>overwritten in place</b>,
/// never blanked in bulk, so a slow or interrupted re-embed narrows search instead of emptying
/// it.</para>
/// </summary>
public sealed class SearchIndexReconciler : ISearchIndexReconciler
{
    private readonly AppDbContext _db;
    private readonly IEmbedder _embedder;
    private readonly SearchIndexOptions _options;
    private readonly SearchIndexMetrics _metrics;
    private readonly ILogger<SearchIndexReconciler> _logger;

    public SearchIndexReconciler(
        AppDbContext db,
        IEmbedder embedder,
        IOptions<SearchIndexOptions> options,
        SearchIndexMetrics metrics,
        ILogger<SearchIndexReconciler> logger)
    {
        _db = db;
        _embedder = embedder;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ReconcileReport> RunOnceAsync(CancellationToken ct = default)
    {
        // Relabel first, before anything is tracked. It is a set-based UPDATE, invisible to the
        // change tracker: run it after SyncChunksAsync has loaded every chunk and the entities in
        // memory still carry the pre-relabel value, so the next SaveChanges would conclude the
        // Model column needed no write and quietly undo it.
        var relabelled = await RelabelLegacyTagsAsync(ct);
        var (inserted, updated, deleted) = await SyncChunksAsync(ct);
        var (embedded, tokens) = await EmbedPendingAsync(ct);
        var coverage = await MeasureCoverageAsync(ct);

        var report = new ReconcileReport(
            inserted, updated, deleted, embedded, tokens, relabelled, coverage);
        if (report.DidWork)
        {
            _logger.LogInformation(
                "Search index reconciled: +{Inserted} ~{Updated} -{Deleted} chunks, {Embedded} embedded "
                + "({Tokens} tokens), {Relabelled} relabelled; {Tag} coverage {Coverage:P0}",
                inserted, updated, deleted, embedded, tokens, relabelled, _embedder.Tag, coverage);
        }

        _metrics.ReportCoverage(coverage, _embedder.Tag);
        return report;
    }

    /// <summary>
    /// Rows the active model already embedded, written before tags existed, restamped with the full
    /// tag. Matching is on the <b>bare</b> model name and nothing else: a row carrying any other
    /// name — another model's, or another provider's tag — is left alone and falls to
    /// <see cref="EmbedPendingAsync"/> as stale.
    ///
    /// <para>This exists so the interim Gemini deployment does not pay for a full re-embed it gains
    /// nothing from: those vectors <i>are</i> <c>gemini-embedding-001</c>'s, and on the free tier
    /// re-making them is about three days of quota (ADR §2 decision 12, revised on EXP-59). It is a
    /// no-op once every row is tagged, which is every pass after the first.</para>
    ///
    /// <para>A set-based update rather than a load-and-save: this runs on every pass forever, and on
    /// a warm index it should cost one statement that matches nothing.</para>
    /// </summary>
    private async Task<int> RelabelLegacyTagsAsync(CancellationToken ct)
    {
        var bare = _embedder.Model;
        var tag = _embedder.Tag;
        if (bare == tag)
        {
            // An untagged embedder (a test fake) — there is no legacy form to distinguish.
            return 0;
        }

        var relabelled = await _db.ExpertSearchChunks
            .Where(c => c.Embedding != null && c.Model == bare)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Model, tag), ct);

        if (relabelled > 0)
        {
            _logger.LogInformation(
                "Search index: relabelled {Count} vector(s) from '{Bare}' to '{Tag}' (no re-embed needed).",
                relabelled, bare, tag);
        }

        return relabelled;
    }

    /// <summary>
    /// Coverage after the pass: chunks carrying the active tag with a vector, over all chunks. One
    /// grouped query, so the reading costs a single round trip. An empty index reads 1 — there is
    /// nothing to rebuild, and reporting 0 would make a brand-new deployment look mid-switch.
    /// </summary>
    private async Task<double> MeasureCoverageAsync(CancellationToken ct)
        => await IndexCoverage.MeasureAsync(_db, _embedder.Tag, ct);

    private async Task<(int Inserted, int Updated, int Deleted)> SyncChunksAsync(CancellationToken ct)
    {
        // Drafts stay out of the index until a human promotes them; the diff below then deletes
        // any chunks belonging to experts that left the Active set (self-healing both ways).
        var experts = await _db.Experts
            .AsNoTracking()
            .Where(e => e.Status == ExpertStatus.Active)
            .Include(e => e.Experiences)
            .ThenInclude(x => x.Achievements)
            .ToListAsync(ct);

        var desired = experts.SelectMany(ChunkProjection.Project).ToList();

        // Tracked so updates/deletes flush without a second lookup.
        var existingEntities = await _db.ExpertSearchChunks.ToListAsync(ct);
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
                _db.ExpertSearchChunks.Add(new ExpertSearchChunk
                {
                    Id = Guid.NewGuid(),
                    ExpertId = upsert.Chunk.ExpertId,
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
                _db.ExpertSearchChunks.Remove(row);
            }
        }

        await _db.SaveChangesAsync(ct);
        return (inserted, updated, diff.Deletes.Count);
    }

    private async Task<(int Embedded, long Tokens)> EmbedPendingAsync(CancellationToken ct)
    {
        // "Wrong tag" is staleness, exactly like "no vector": one condition heals a cold index, an
        // edited chunk and a provider switch alike.
        var tag = _embedder.Tag;
        var pending = await _db.ExpertSearchChunks
            .Where(c => c.Embedding == null || c.Model != tag)
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
                // Overwritten in place. A stale vector stays queryable (under its old tag, so no
                // search will compare against it) until the moment its replacement lands, which is
                // what stops an interrupted re-embed from emptying the index.
                batch[i].Embedding = new Vector(result.Vectors[i]);
                batch[i].Model = tag;
                batch[i].EmbeddedAt = now;
                embedded++;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Search index: embedded {Done}/{Total} pending chunk(s) with {Tag}.",
                embedded, pending.Count, tag);
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
