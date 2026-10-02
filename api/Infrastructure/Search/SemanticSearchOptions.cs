using ExpertToJob.Infrastructure.Embeddings;

namespace ExpertToJob.Infrastructure.Search;

/// <summary>Tuning for the semantic roster search query (ranking guardrails).
///
/// <para>Only the similarity floor is a value: the sizes below are constants (EXP-106). They were
/// bindable, and nothing bound them — no settings file, no AppHost parameter, no host — so each was
/// a second place its number could live, invisible to the compiler and unnoticed by the tests that
/// pin the payloads they shape. <c>Application.Tests/SearchSizeConstantTests</c> holds the numbers;
/// the <c>CostFloors</c> suites measure what they cost.</para></summary>
public sealed class SemanticSearchOptions
{
    /// <summary>Minimum cosine similarity (0–1) for a chunk to count as a match. Below this it is
    /// dropped, so an off-topic query returns nothing rather than the least-bad rows.
    ///
    /// <para><b>Not configured here.</b> The floor is calibrated per embedding model, so since
    /// EXP-64 it comes out of the active provider's own block and <c>AddSearchIndexing</c> writes it
    /// over whatever this default holds; a leftover <c>SemanticSearch:MinSimilarity</c> throws at
    /// startup. The default below is the incumbent's value, because an absent
    /// <c>Ai:Embeddings:Provider</c> means Gemini — read from the provider's own defaults rather
    /// than spelled again, so the two cannot drift.</para></summary>
    public double MinSimilarity { get; set; } =
        EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini).MinSimilarity;

    /// <summary>Default number of experts returned when the caller doesn't specify.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Hard cap on experts returned, whatever the caller asks for.</summary>
    public const int MaxTopK = 20;

    /// <summary>Default number of shortlist candidates returned when the caller doesn't specify.</summary>
    public const int ShortlistDefaultTopK = 10;

    /// <summary>Hard cap on shortlist candidates returned, whatever the caller asks for.</summary>
    public const int ShortlistMaxTopK = 20;

    /// <summary>Max snippets returned per expert (the closest-matching chunks).</summary>
    public const int MaxSnippetsPerExpert = 3;

    /// <summary>Snippet text is truncated to this many characters to keep tool payloads small.</summary>
    public const int SnippetMaxChars = 500;

    /// <summary>Default number of style exemplars returned per requested bullet.</summary>
    public const int ExemplarsPerBullet = 2;

    /// <summary>Hard cap on exemplars per bullet, whatever the caller asks for.</summary>
    public const int ExemplarsPerBulletMax = 5;

    /// <summary>Bullets shorter than this carry no imitable style; excluded from exemplars.</summary>
    public const int ExemplarMinChars = 40;

    /// <summary>Bullets longer than this are paragraphs, not bullets; excluded from exemplars.</summary>
    public const int ExemplarMaxChars = 300;
}
