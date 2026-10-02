namespace ExpertToJob.Application.Search;

/// <summary>
/// How one shortlist candidate fared against one requirement: matched or not, and when matched,
/// the best evidence snippet with its similarity.
/// </summary>
public sealed record ShortlistRequirementEvidence(
    string Requirement,
    bool Matched,
    string? Snippet = null,
    double? Similarity = null);

/// <summary>
/// One shortlisted expert: coverage (how many requirements they matched), a composite score, and
/// per-requirement evidence.
/// </summary>
public sealed record ShortlistCandidate(
    Guid ExpertId,
    string Name,
    string Title,
    double Score,
    int MatchedCount,
    int TotalRequirements,
    IReadOnlyList<ShortlistRequirementEvidence> Evidence);

/// <summary>
/// The published <c>roster_shortlist_search</c> tool result, and the shape the service itself
/// returns: coverage-ranked candidates, or the fault that stopped retrieval — <c>error</c> is
/// present and null on success, and an empty <c>results</c> with no error is a perfectly good
/// answer (nobody matched). This is the wire shape, and nothing converts to or from it, so the
/// JSON agents already parse stays byte for byte what it was. <c>RosterShortlistToolsTests</c>
/// asserts it literally.
/// </summary>
public sealed record ShortlistSearchResult(IReadOnlyList<ShortlistCandidate> Results, string? Error = null)
{
    /// <summary>A successful search that found nobody.</summary>
    public static ShortlistSearchResult None { get; } = new([]);
}

/// <summary>
/// Multi-requirement retrieval for JD-driven shortlisting: embed every requirement, match each
/// against the (optionally pre-filtered) roster, and merge coverage-first — candidates matching
/// more requirements rank above narrower, higher-similarity ones.
/// </summary>
public interface IShortlistSearchService
{
    Task<ShortlistSearchResult> SearchAsync(
        IReadOnlyList<string> requirements,
        SemanticSearchFilters? filters = null,
        int? topK = null,
        CancellationToken ct = default);
}
