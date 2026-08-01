namespace CvManager.Application.Search;

/// <summary>One anonymized strong-phrasing exemplar: the scrubbed bullet text and how close it
/// sits to the requested bullet (cosine similarity, 0–1).</summary>
public sealed record StyleExemplar(string Text, double Similarity);

/// <summary>The exemplars retrieved for one requested achievement bullet. Empty when nothing
/// relevant clears the similarity floor.</summary>
public sealed record BulletExemplars(Guid AchievementId, IReadOnlyList<StyleExemplar> Exemplars);

/// <summary>
/// Result of an exemplar search. <see cref="Error"/> is non-null only when retrieval could not run
/// (e.g. the embedding backend failed); callers degrade gracefully rather than surfacing an error.
/// </summary>
public sealed record ExemplarSearchResult(IReadOnlyList<BulletExemplars> Results, string? Error = null)
{
    public static ExemplarSearchResult Empty { get; } = new([]);
    public static ExemplarSearchResult Failed(string error) => new([], error);
}

/// <summary>
/// Id-keyed style exemplar retrieval for CV tailoring: given achievement ids (e.g. from cv_get),
/// resolve each bullet's stored text server-side, and return — per bullet — the closest quantified
/// achievement bullets from OTHER employees' CVs, anonymized ([name]/[company] placeholders) so
/// they can be imitated for phrasing style without leaking who wrote them. Unknown ids are skipped
/// silently; the same source bullet is never returned twice within one request.
/// </summary>
public interface IExemplarSearchService
{
    Task<ExemplarSearchResult> SearchAsync(
        IReadOnlyList<Guid> achievementIds,
        int? topKPerBullet = null,
        CancellationToken ct = default);
}
