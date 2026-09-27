using System.Diagnostics.Metrics;

namespace ExpertToJob.Infrastructure.Search;

/// <summary>
/// The search index's own instruments. One gauge today: how much of the index the active embedder
/// has actually embedded (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 13).
///
/// <para>A provider switch is the one operation where the index is knowingly wrong for a while, and
/// the only question anyone asks during it is "how far along is it". The reconciler answers that
/// every pass. A plain <see cref="Gauge{T}"/> rather than an observable one because the value is
/// only knowable right after a pass has counted it: an observable callback would have to open its
/// own database scope on the collector's thread to answer.</para>
/// </summary>
public sealed class SearchIndexMetrics : IDisposable
{
    /// <summary>Subscribed by the MCP host and frozen in <c>HostTelemetryFreezeTests</c>.</summary>
    public const string MeterName = "ExpertToJob.Search";

    /// <summary>Chunks carrying the active tag with a vector, over all chunks. 1 means the index is
    /// fully on the current model.</summary>
    public const string CoverageInstrumentName = "experttojob.search.index.coverage";

    private readonly Meter _meter;
    private readonly Gauge<double> _coverage;

    public SearchIndexMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _coverage = _meter.CreateGauge<double>(
            CoverageInstrumentName,
            unit: "1",
            description: "Fraction of search chunks embedded by the active provider/model.");
    }

    /// <summary>Publish one reading, tagged with the embedder it was measured against — the number
    /// means nothing without knowing which tag was counted as current.</summary>
    public void ReportCoverage(double coverage, string tag)
        => _coverage.Record(coverage, new KeyValuePair<string, object?>("embedding.tag", tag));

    public void Dispose() => _meter.Dispose();
}
