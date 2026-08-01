namespace CvManager.Infrastructure.Search;

/// <summary>Tuning for the semantic roster search query (ranking guardrails).</summary>
public sealed class SemanticSearchOptions
{
    public const string Section = "SemanticSearch";

    /// <summary>Minimum cosine similarity (0–1) for a chunk to count as a match. Below this it is
    /// dropped, so an off-topic query returns nothing rather than the least-bad rows.</summary>
    public double MinSimilarity { get; set; } = 0.30;

    /// <summary>Default number of employees returned when the caller doesn't specify.</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Hard cap on employees returned, whatever the caller asks for.</summary>
    public int MaxTopK { get; set; } = 20;

    /// <summary>Default number of shortlist candidates returned when the caller doesn't specify.</summary>
    public int ShortlistDefaultTopK { get; set; } = 10;

    /// <summary>Hard cap on shortlist candidates returned, whatever the caller asks for.</summary>
    public int ShortlistMaxTopK { get; set; } = 20;

    /// <summary>Max snippets returned per employee (the closest-matching chunks).</summary>
    public int MaxSnippetsPerEmployee { get; set; } = 3;

    /// <summary>Snippet text is truncated to this many characters to keep tool payloads small.</summary>
    public int SnippetMaxChars { get; set; } = 500;

    /// <summary>Default number of style exemplars returned per requested bullet.</summary>
    public int ExemplarsPerBullet { get; set; } = 2;

    /// <summary>Hard cap on exemplars per bullet, whatever the caller asks for.</summary>
    public int ExemplarsPerBulletMax { get; set; } = 5;

    /// <summary>Bullets shorter than this carry no imitable style; excluded from exemplars.</summary>
    public int ExemplarMinChars { get; set; } = 40;

    /// <summary>Bullets longer than this are paragraphs, not bullets; excluded from exemplars.</summary>
    public int ExemplarMaxChars { get; set; } = 300;
}
