namespace ExpertToJob.Application.Search;

/// <summary>One anonymized strong-phrasing exemplar: the scrubbed bullet text and how close it
/// sits to the requested bullet or theme (cosine similarity, 0–1).</summary>
public sealed record StyleExemplar(string Text, double Similarity);

/// <summary>The exemplars retrieved for one requested achievement bullet (id-keyed mode). Empty
/// when nothing relevant clears the similarity floor.</summary>
public sealed record BulletExemplars(Guid AchievementId, IReadOnlyList<StyleExemplar> Exemplars);

/// <summary>The exemplars retrieved for a free-text theme (id-less mode) — a sibling to
/// <see cref="BulletExemplars"/> rather than a nullable-keyed variant of it, so the response shape
/// states honestly which mode produced it.</summary>
public sealed record ThemeExemplars(string Theme, IReadOnlyList<StyleExemplar> Exemplars);

/// <summary>
/// Result of an exemplar search. Exactly one of <see cref="Results"/> (id-keyed mode) or
/// <see cref="ThemeResult"/> (theme mode) is populated, matching whichever mode the request used.
/// <see cref="Error"/> is non-null only when retrieval could not run (e.g. the embedding backend
/// failed); callers degrade gracefully rather than surfacing an error.
/// </summary>
public sealed record ExemplarSearchResult(
    IReadOnlyList<BulletExemplars> Results, ThemeExemplars? ThemeResult = null, string? Error = null)
{
    public static ExemplarSearchResult Empty { get; } = new([]);
    public static ExemplarSearchResult Failed(string error) => new([], null, error);
}

/// <summary>
/// Style exemplar retrieval for CV tailoring, in two mutually exclusive modes: id-keyed — given
/// achievement ids (e.g. from cv_get), resolve each bullet's stored text server-side and return,
/// per bullet, the closest quantified achievement bullets from OTHER experts' CVs; or themed —
/// given a free-text theme with no bullet to anchor to, embed the theme itself and return the
/// closest quantified achievement bullets against it (there is no requesting expert to exclude,
/// so nothing is subtracted from the pool). Either way results are anonymized
/// ([name]/[company] placeholders) so they can be imitated for phrasing style without leaking who
/// wrote them. Unknown ids are skipped silently; the same source bullet is never returned twice
/// within one request.
/// </summary>
public interface IExemplarSearchService
{
    Task<ExemplarSearchResult> SearchAsync(
        IReadOnlyList<Guid>? achievementIds,
        string? theme = null,
        int? topKPerBullet = null,
        CancellationToken ct = default);
}
