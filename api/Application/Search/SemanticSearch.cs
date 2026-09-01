namespace ExpertToJob.Application.Search;

/// <summary>
/// Optional hard constraints AND-ed into a semantic search before ranking, so the returned people
/// are all valid candidates (not just the closest by meaning). All are optional.
/// </summary>
public sealed record SemanticSearchFilters(
    /// <summary>Keep only experts with capacity &gt; 0 on this date (availability step-function).</summary>
    DateOnly? AvailableOn = null,
    /// <summary>Keep only experts who have every one of these catalog skills.</summary>
    IReadOnlyList<Guid>? SkillIds = null,
    /// <summary>Keep only experts whose location matches (case-insensitive).</summary>
    string? Location = null,
    /// <summary>Minimum years of experience — applied to the required skills, or to any skill if none given.</summary>
    decimal? MinYears = null);

/// <summary>One matched expert: the ranking score plus the evidence snippets that matched.</summary>
public sealed record SemanticSearchHit(
    Guid ExpertId,
    string Name,
    string Title,
    double Score,
    IReadOnlyList<string> Snippets);

/// <summary>
/// Result of a semantic search. <see cref="Error"/> is non-null only when retrieval could not run
/// (e.g. the embedding backend failed); callers degrade gracefully rather than surfacing an error.
/// <see cref="DegradedReason"/> is non-null when retrieval DID run but without semantic ranking
/// (keyword fallback while the embedding quota is exhausted) — results are real matches, scores are
/// lexical ranks, and the caller should tell the user ranking quality is reduced.
/// </summary>
public sealed record SemanticSearchResult(
    IReadOnlyList<SemanticSearchHit> Results,
    string? Error = null,
    string? DegradedReason = null)
{
    public static SemanticSearchResult Empty { get; } = new([]);
    public static SemanticSearchResult Failed(string error) => new([], error);
}

/// <summary>
/// Retrieval over expert career narratives: embed the query, rank chunks by cosine similarity
/// within the (optionally) pre-filtered candidate set, and aggregate to the best-matching experts
/// with their evidence snippets.
/// </summary>
public interface ISemanticSearchService
{
    Task<SemanticSearchResult> SearchAsync(
        string query,
        SemanticSearchFilters? filters = null,
        int? topK = null,
        CancellationToken ct = default);
}
