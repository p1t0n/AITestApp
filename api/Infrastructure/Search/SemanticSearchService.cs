using CvManager.Application.Abstractions;
using CvManager.Application.Search;
using CvManager.Domain.Entities;
using CvManager.Domain.Enums;
using CvManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace CvManager.Infrastructure.Search;

/// <summary>
/// pgvector-backed semantic roster search. Embeds the query, applies the optional hard filters as a
/// SQL pre-filter (so the top-K are all valid candidates), ranks chunks by cosine similarity, and
/// aggregates chunk hits to employees (best similarity + evidence snippets).
/// </summary>
public sealed class SemanticSearchService : ISemanticSearchService, IShortlistSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbedder _embedder;
    private readonly SemanticSearchOptions _options;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        AppDbContext db,
        IEmbedder embedder,
        IOptions<SemanticSearchOptions> options,
        ILogger<SemanticSearchService> logger)
    {
        _db = db;
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SemanticSearchResult> SearchAsync(
        string query, SemanticSearchFilters? filters = null, int? topK = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return SemanticSearchResult.Empty;
        }

        var limit = Math.Clamp(topK ?? _options.DefaultTopK, 1, _options.MaxTopK);

        // Embed the query. Retrieval failing must not fault the caller — return a soft error so the
        // agent can fall back to structured tools.
        Vector queryVector;
        try
        {
            var embedded = await _embedder.EmbedAsync([query], ct);
            queryVector = new Vector(embedded.Vectors[0]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search could not embed the query; returning a soft error.");
            return SemanticSearchResult.Failed("The semantic search backend is unavailable.");
        }

        var eligibleIds = await ResolveEligibleEmployeesAsync(filters, ct);
        if (eligibleIds is { Count: 0 })
        {
            return SemanticSearchResult.Empty; // filters excluded everyone
        }

        var ranked = await RankChunksAsync(queryVector, eligibleIds, Fetch(limit), ct);

        if (ranked.Count == 0)
        {
            return SemanticSearchResult.Empty;
        }

        var byEmployee = ranked
            .GroupBy(x => x.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                BestDistance = g.Min(x => x.Distance),
                Snippets = g.OrderBy(x => x.Distance)
                    .Take(_options.MaxSnippetsPerEmployee)
                    .Select(x => Truncate(x.Content))
                    .ToList(),
            })
            .OrderBy(x => x.BestDistance)
            .Take(limit)
            .ToList();

        var ids = byEmployee.Select(x => x.EmployeeId).ToList();
        var employees = await _db.Employees
            .Where(e => ids.Contains(e.Id) && e.Status == EmployeeStatus.Active)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.Title })
            .ToDictionaryAsync(e => e.Id, ct);

        var hits = byEmployee
            .Where(x => employees.ContainsKey(x.EmployeeId))
            .Select(x =>
            {
                var e = employees[x.EmployeeId];
                return new SemanticSearchHit(
                    x.EmployeeId,
                    $"{e.FirstName} {e.LastName}".Trim(),
                    e.Title,
                    Math.Round(1.0 - x.BestDistance, 4),
                    x.Snippets);
            })
            .ToList();

        return new SemanticSearchResult(hits);
    }

    /// <summary>
    /// Multi-requirement shortlist search: embed all requirements in one batch, run one cosine query
    /// per requirement over the pre-filtered chunk set, then merge coverage-first via
    /// <see cref="ShortlistRanker"/> (requirements matched before similarity).
    /// </summary>
    public async Task<ShortlistSearchResult> SearchAsync(
        IReadOnlyList<string> requirements, SemanticSearchFilters? filters = null, int? topK = null,
        CancellationToken ct = default)
    {
        var cleaned = (requirements ?? []).Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
        if (cleaned.Count == 0)
        {
            return ShortlistSearchResult.Empty;
        }

        var limit = Math.Clamp(topK ?? _options.ShortlistDefaultTopK, 1, _options.ShortlistMaxTopK);

        // One batched embed call for all requirements. Retrieval failing must not fault the caller —
        // return a soft error so the agent can fall back to structured tools.
        List<Vector> requirementVectors;
        try
        {
            var embedded = await _embedder.EmbedAsync(cleaned, ct);
            requirementVectors = embedded.Vectors.Select(v => new Vector(v)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shortlist search could not embed the requirements; returning a soft error.");
            return ShortlistSearchResult.Failed("The semantic search backend is unavailable.");
        }

        var eligibleIds = await ResolveEligibleEmployeesAsync(filters, ct);
        if (eligibleIds is { Count: 0 })
        {
            return ShortlistSearchResult.Empty; // filters excluded everyone
        }

        // One cosine query per requirement; keep each employee's best chunk as that requirement's match.
        var matchesPerRequirement = new List<IReadOnlyDictionary<Guid, ShortlistMatch>>(cleaned.Count);
        foreach (var requirementVector in requirementVectors)
        {
            var ranked = await RankChunksAsync(requirementVector, eligibleIds, Fetch(limit), ct);

            matchesPerRequirement.Add(ranked
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var best = g.MinBy(x => x.Distance)!;
                        return new ShortlistMatch(1.0 - best.Distance, Truncate(best.Content));
                    }));
        }

        var merged = ShortlistRanker.Rank(cleaned, matchesPerRequirement, limit);
        if (merged.Count == 0)
        {
            return ShortlistSearchResult.Empty;
        }

        var ids = merged.Select(x => x.EmployeeId).ToList();
        var employees = await _db.Employees
            .Where(e => ids.Contains(e.Id) && e.Status == EmployeeStatus.Active)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.Title })
            .ToDictionaryAsync(e => e.Id, ct);

        var candidatesRanked = merged
            .Where(x => employees.ContainsKey(x.EmployeeId))
            .Select(x =>
            {
                var e = employees[x.EmployeeId];
                return new ShortlistCandidate(
                    x.EmployeeId,
                    $"{e.FirstName} {e.LastName}".Trim(),
                    e.Title,
                    x.Score,
                    x.MatchedCount,
                    cleaned.Count,
                    x.Evidence);
            })
            .ToList();

        return new ShortlistSearchResult(candidatesRanked);
    }

    /// <summary>Chunks ranked by cosine distance to one query vector, capped and thresholded.</summary>
    private sealed record ChunkHit(Guid EmployeeId, string Content, double Distance);

    /// <summary>
    /// Ranks the (optionally pre-filtered) chunk set against one query vector: closest first,
    /// anything below <see cref="SemanticSearchOptions.MinSimilarity"/> dropped, at most
    /// <paramref name="fetch"/> rows.
    /// </summary>
    private async Task<List<ChunkHit>> RankChunksAsync(
        Vector queryVector, HashSet<Guid>? eligibleIds, int fetch, CancellationToken ct)
    {
        var maxDistance = 1.0 - _options.MinSimilarity;

        // Achievement bullet chunks are excluded from the employee-level retrieval pool: they are
        // the fine-grained unit for exemplar retrieval, and the live eval showed they raise the
        // negative-false-positive rate when they compete here (their narrative already reaches
        // these paths rolled into the parent experience chunk).
        var candidates = _db.EmployeeSearchChunks
            .Where(c => c.Embedding != null && c.SourceType != SearchChunkSource.Achievement);
        if (eligibleIds is not null)
        {
            candidates = candidates.Where(c => eligibleIds.Contains(c.EmployeeId));
        }

        // Project to an anonymous type inside the query (EF can't translate record constructors).
        var rows = await candidates
            .Select(c => new { c.EmployeeId, c.Content, Distance = c.Embedding!.CosineDistance(queryVector) })
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(fetch)
            .ToListAsync(ct);

        return rows.Select(x => new ChunkHit(x.EmployeeId, x.Content, x.Distance)).ToList();
    }

    /// <summary>Over-fetch chunk rows relative to the requested employee count, since several
    /// chunks can belong to the same employee.</summary>
    private static int Fetch(int limit) => Math.Max(limit * 10, 50);

    /// <summary>
    /// Ids of employees passing the hard filters, or null when no filter is set (no restriction).
    /// Everything here is SQL-translatable, including the availability step-function
    /// (latest entry on/before the date, capacity &gt; 0).
    /// </summary>
    private async Task<HashSet<Guid>?> ResolveEligibleEmployeesAsync(SemanticSearchFilters? filters, CancellationToken ct)
    {
        if (filters is null)
        {
            return null;
        }

        var hasSkills = filters.SkillIds is { Count: > 0 };
        var hasFilter = filters.AvailableOn is not null
            || !string.IsNullOrWhiteSpace(filters.Location)
            || hasSkills
            || filters.MinYears is not null;
        if (!hasFilter)
        {
            return null;
        }

        IQueryable<Employee> q = _db.Employees.Where(e => e.Status == EmployeeStatus.Active);

        if (!string.IsNullOrWhiteSpace(filters.Location))
        {
            var loc = filters.Location.Trim().ToLower();
            q = q.Where(e => e.Location != null && e.Location.ToLower() == loc);
        }

        if (hasSkills)
        {
            // Must have every requested skill; if MinYears is set, each must meet it.
            foreach (var skillId in filters.SkillIds!)
            {
                var min = filters.MinYears;
                q = q.Where(e => e.Skills.Any(s =>
                    s.SkillId == skillId && (min == null || s.YearsExperience >= min)));
            }
        }
        else if (filters.MinYears is { } minYears)
        {
            q = q.Where(e => e.Skills.Any(s => s.YearsExperience >= minYears));
        }

        if (filters.AvailableOn is { } onDate)
        {
            q = q.Where(e => e.AvailabilityEntries
                .Where(a => a.EffectiveFrom <= onDate)
                .OrderByDescending(a => a.EffectiveFrom)
                .Select(a => a.CapacityPercent)
                .FirstOrDefault() > 0);
        }

        var ids = await q.Select(e => e.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }

    private string Truncate(string text)
        => text.Length <= _options.SnippetMaxChars
            ? text
            : text[.._options.SnippetMaxChars] + "…";
}
