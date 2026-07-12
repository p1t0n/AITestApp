using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Search;
using EmployeeManager.Domain.Enums;
using EmployeeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace EmployeeManager.Infrastructure.Search;

/// <summary>
/// pgvector-backed style exemplar retrieval. Resolves each requested achievement bullet's stored
/// text server-side (unknown ids skipped), embeds all bullets in one batch, and per bullet ranks
/// OTHER employees' achievement-bullet chunks by cosine similarity — keeping only quantified
/// bullets inside the configured length band, never repeating a source bullet within one request —
/// then anonymizes each exemplar (source names/companies scrubbed) before returning it.
/// </summary>
public sealed class ExemplarSearchService : IExemplarSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbedder _embedder;
    private readonly SemanticSearchOptions _options;
    private readonly ILogger<ExemplarSearchService> _logger;

    public ExemplarSearchService(
        AppDbContext db,
        IEmbedder embedder,
        IOptions<SemanticSearchOptions> options,
        ILogger<ExemplarSearchService> logger)
    {
        _db = db;
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExemplarSearchResult> SearchAsync(
        IReadOnlyList<Guid> achievementIds, int? topKPerBullet = null, CancellationToken ct = default)
    {
        var requested = (achievementIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (requested.Count == 0)
        {
            return ExemplarSearchResult.Empty;
        }

        // Resolve the stored bullet text + owner server-side; ids that don't exist drop out here.
        var found = await _db.Achievements
            .Where(a => requested.Contains(a.Id))
            .Select(a => new { a.Id, a.Text, OwnerId = a.Experience.EmployeeId })
            .ToListAsync(ct);
        var foundById = found.ToDictionary(b => b.Id);
        var bullets = requested.Where(foundById.ContainsKey).Select(id => foundById[id]).ToList();
        if (bullets.Count == 0)
        {
            return ExemplarSearchResult.Empty;
        }

        var perBullet = Math.Clamp(
            topKPerBullet ?? _options.ExemplarsPerBullet, 1, _options.ExemplarsPerBulletMax);

        // One batched embed call for all resolved bullets. Retrieval failing must not fault the
        // caller — return a soft error so the agent can proceed without exemplars.
        List<Vector> bulletVectors;
        try
        {
            var embedded = await _embedder.EmbedAsync(bullets.Select(b => b.Text).ToList(), ct);
            bulletVectors = embedded.Vectors.Select(v => new Vector(v)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exemplar search could not embed the bullets; returning a soft error.");
            return ExemplarSearchResult.Failed("The semantic search backend is unavailable.");
        }

        var maxDistance = 1.0 - _options.MinSimilarity;
        var usedSourceIds = new HashSet<Guid>(); // a source bullet is returned at most once per request
        var picks = new List<(Guid AchievementId, List<ExemplarHit> Hits)>(bullets.Count);

        for (var i = 0; i < bullets.Count; i++)
        {
            var bullet = bullets[i];
            var bulletVector = bulletVectors[i];

            // Over-fetch: the quantified-quality gate and the cross-request dedupe run in memory.
            var fetch = perBullet * 5 + 20;
            var rows = await _db.EmployeeSearchChunks
                .Where(c => c.Embedding != null
                    && c.SourceType == SearchChunkSource.Achievement
                    && c.EmployeeId != bullet.OwnerId
                    && c.Content.Length >= _options.ExemplarMinChars
                    && c.Content.Length <= _options.ExemplarMaxChars)
                .Select(c => new
                {
                    c.SourceId,
                    c.EmployeeId,
                    c.Content,
                    Distance = c.Embedding!.CosineDistance(bulletVector),
                })
                .Where(x => x.Distance <= maxDistance)
                .OrderBy(x => x.Distance)
                .Take(fetch)
                .ToListAsync(ct);

            // Closest first; a bullet claims a source exemplar only if no earlier bullet took it.
            var hits = rows
                .Where(x => ExemplarQualityFilter.Passes(x.Content, _options.ExemplarMinChars, _options.ExemplarMaxChars))
                .Where(x => usedSourceIds.Add(x.SourceId))
                .Take(perBullet)
                .Select(x => new ExemplarHit(x.EmployeeId, x.Content, x.Distance))
                .ToList();

            picks.Add((bullet.Id, hits));
        }

        // Anonymization inputs for every source employee we are about to quote: their name and all
        // their employers' names. The scrub runs before any text leaves this service.
        var sourceEmployeeIds = picks.SelectMany(p => p.Hits).Select(h => h.EmployeeId).Distinct().ToList();
        var sources = await _db.Employees
            .Where(e => sourceEmployeeIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                Companies = e.Experiences.Select(x => x.Company).ToList(),
            })
            .ToDictionaryAsync(e => e.Id, ct);

        var groups = picks
            .Select(p => new BulletExemplars(
                p.AchievementId,
                p.Hits.Select(h =>
                {
                    var source = sources[h.EmployeeId];
                    return new StyleExemplar(
                        ExemplarAnonymizer.Scrub(h.Content, source.FirstName, source.LastName, source.Companies),
                        Math.Round(1.0 - h.Distance, 4));
                }).ToList()))
            .ToList();

        return new ExemplarSearchResult(groups);
    }

    private sealed record ExemplarHit(Guid EmployeeId, string Content, double Distance);
}
