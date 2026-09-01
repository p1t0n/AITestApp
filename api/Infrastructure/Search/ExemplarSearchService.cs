using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Application.Search;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace ExpertToJob.Infrastructure.Search;

/// <summary>
/// pgvector-backed style exemplar retrieval, in two mutually exclusive modes. Id-keyed: resolves
/// each requested achievement bullet's stored text server-side (unknown ids skipped), embeds all
/// bullets in one batch, and per bullet ranks OTHER experts' achievement-bullet chunks by cosine
/// similarity. Themed: embeds the free-text theme itself and ranks the whole achievement-bullet
/// pool against it (no owner to exclude). Both modes keep only quantified bullets inside the
/// configured length band, never repeat a source bullet within one request, and anonymize each
/// exemplar (source names/companies scrubbed) before returning it.
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
        IReadOnlyList<Guid>? achievementIds,
        string? theme = null,
        int? topKPerBullet = null,
        CancellationToken ct = default)
    {
        var requested = (achievementIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        var trimmedTheme = theme?.Trim();
        var hasTheme = !string.IsNullOrEmpty(trimmedTheme);
        if (requested.Count > 0 == hasTheme)
        {
            throw new ValidationException(ExclusivityFailures(requested.Count > 0, hasTheme));
        }

        var topK = Math.Clamp(topKPerBullet ?? _options.ExemplarsPerBullet, 1, _options.ExemplarsPerBulletMax);

        return hasTheme
            ? await SearchByThemeAsync(trimmedTheme!, topK, ct)
            : await SearchByIdsAsync(requested, topK, ct);
    }

    private static List<ValidationFailure> ExclusivityFailures(bool hasIds, bool hasTheme)
    {
        var message = hasIds && hasTheme
            ? "Provide either achievementIds or theme, not both."
            : "Provide either achievementIds or theme.";
        return [new ValidationFailure("achievementIds", message), new ValidationFailure("theme", message)];
    }

    private async Task<ExemplarSearchResult> SearchByIdsAsync(
        IReadOnlyList<Guid> requested, int perBullet, CancellationToken ct)
    {
        // Resolve the stored bullet text + owner server-side; ids that don't exist drop out here.
        var found = await _db.Achievements
            .Where(a => requested.Contains(a.Id))
            .Select(a => new { a.Id, a.Text, OwnerId = a.Experience.ExpertId })
            .ToListAsync(ct);
        var foundById = found.ToDictionary(b => b.Id);
        var bullets = requested.Where(foundById.ContainsKey).Select(id => foundById[id]).ToList();
        if (bullets.Count == 0)
        {
            return ExemplarSearchResult.Empty;
        }

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
            var rows = await _db.ExpertSearchChunks
                .Where(c => c.Embedding != null
                    && c.SourceType == SearchChunkSource.Achievement
                    && c.ExpertId != bullet.OwnerId
                    // Anonymised or not, it is still a paused person's own writing being put to
                    // work (P1T-185). The seam decides, here as everywhere else.
                    && _db.Experts.OnTheBench().Any(e => e.Id == c.ExpertId)
                    && c.Content.Length >= _options.ExemplarMinChars
                    && c.Content.Length <= _options.ExemplarMaxChars)
                .Select(c => new
                {
                    c.SourceId,
                    c.ExpertId,
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
                .Select(x => new ExemplarHit(x.ExpertId, x.Content, x.Distance))
                .ToList();

            picks.Add((bullet.Id, hits));
        }

        var sources = await LoadSourcesAsync(picks.SelectMany(p => p.Hits).Select(h => h.ExpertId), ct);

        var groups = picks
            .Select(p => new BulletExemplars(p.AchievementId, ToExemplars(p.Hits, sources)))
            .ToList();

        return new ExemplarSearchResult(groups);
    }

    private async Task<ExemplarSearchResult> SearchByThemeAsync(string theme, int topK, CancellationToken ct)
    {
        Vector themeVector;
        try
        {
            var embedded = await _embedder.EmbedAsync([theme], ct);
            themeVector = new Vector(embedded.Vectors[0]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exemplar search could not embed the theme; returning a soft error.");
            return ExemplarSearchResult.Failed("The semantic search backend is unavailable.");
        }

        var maxDistance = 1.0 - _options.MinSimilarity;
        // No requesting expert, so there is no owner to exclude — every expert's bullets are
        // eligible, unlike the id-keyed path.
        var fetch = topK * 5 + 20;
        var rows = await _db.ExpertSearchChunks
            .Where(c => c.Embedding != null
                && c.SourceType == SearchChunkSource.Achievement
                && c.Content.Length >= _options.ExemplarMinChars
                && c.Content.Length <= _options.ExemplarMaxChars)
            .Select(c => new
            {
                c.SourceId,
                c.ExpertId,
                c.Content,
                Distance = c.Embedding!.CosineDistance(themeVector),
            })
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(fetch)
            .ToListAsync(ct);

        var usedSourceIds = new HashSet<Guid>();
        var hits = rows
            .Where(x => ExemplarQualityFilter.Passes(x.Content, _options.ExemplarMinChars, _options.ExemplarMaxChars))
            .Where(x => usedSourceIds.Add(x.SourceId))
            .Take(topK)
            .Select(x => new ExemplarHit(x.ExpertId, x.Content, x.Distance))
            .ToList();

        var sources = await LoadSourcesAsync(hits.Select(h => h.ExpertId), ct);

        return new ExemplarSearchResult([], new ThemeExemplars(theme, ToExemplars(hits, sources)));
    }

    // Anonymization inputs for every source expert we are about to quote: their name and all
    // their employers' names. The scrub runs before any text leaves this service.
    private async Task<Dictionary<Guid, ExemplarSource>> LoadSourcesAsync(
        IEnumerable<Guid> expertIds, CancellationToken ct)
    {
        var ids = expertIds.Distinct().ToList();
        return await _db.Experts
            .OnTheBench()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new ExemplarSource(
                e.Id, e.FirstName, e.LastName, e.Experiences.Select(x => x.Company).ToList()))
            .ToDictionaryAsync(e => e.Id, ct);
    }

    /// <summary>
    /// Drops a hit whose source did not come back rather than indexing into the dictionary blind.
    /// The two queries are moments apart and somebody can pause themselves in between (P1T-185) —
    /// and the failure mode of the blind lookup is not a leak but a 500, which is worse: the
    /// scrub inputs are exactly the name and employers this text has to have removed, so an
    /// exemplar with no source must not be emitted at all.
    /// </summary>
    private static List<StyleExemplar> ToExemplars(
        IReadOnlyList<ExemplarHit> hits, IReadOnlyDictionary<Guid, ExemplarSource> sources)
        => hits
            .Where(h => sources.ContainsKey(h.ExpertId))
            .Select(h =>
            {
                var source = sources[h.ExpertId];
                return new StyleExemplar(
                    ExemplarAnonymizer.Scrub(h.Content, source.FirstName, source.LastName, source.Companies),
                    Math.Round(1.0 - h.Distance, 4));
            }).ToList();

    private sealed record ExemplarHit(Guid ExpertId, string Content, double Distance);

    private sealed record ExemplarSource(Guid Id, string FirstName, string LastName, List<string> Companies);
}
