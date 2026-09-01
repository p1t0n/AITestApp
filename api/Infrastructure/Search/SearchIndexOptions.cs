namespace ExpertToJob.Infrastructure.Search;

/// <summary>
/// Tuning for the semantic-search reconciliation worker. The first run finds every chunk
/// unembedded and fills it, so this same loop both backfills and keeps the index fresh.
/// </summary>
public sealed class SearchIndexOptions
{
    public const string Section = "SearchIndex";

    /// <summary>Master switch. When false the worker never runs (e.g. in tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between reconciliation passes.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>How many chunks to embed per provider call.</summary>
    public int EmbedBatchSize { get; set; } = 32;

    /// <summary>Seconds to wait after a pass that died on embedding-quota exhaustion. The daily
    /// free-tier cap won't clear on the normal tick, and retrying anyway burns the next day's
    /// allowance on guaranteed failures (P1T-98) — so this is deliberately long.</summary>
    public int QuotaBackoffSeconds { get; set; } = 1800;
}
