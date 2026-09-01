using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Search;
using ExpertToJob.Mcp.Search;
using FluentAssertions;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Unit tests for the worker's pass-to-pass delay decision (pure, tested directly — same pattern
/// as <c>GeminiCompatHandler.NormalizeFinishReasons</c>). A quota-exhausted pass must back off for
/// <see cref="SearchIndexOptions.QuotaBackoffSeconds"/> instead of hammering the provider on the
/// normal tick (P1T-98: a 30s tick with in-embedder retries burned the whole daily quota on
/// guaranteed failures).
/// </summary>
public class ReconcileWorkerTests
{
    private static readonly SearchIndexOptions Options = new()
    {
        IntervalSeconds = 30,
        QuotaBackoffSeconds = 1800,
    };

    [Fact]
    public void Successful_pass_waits_the_normal_interval()
        => ReconcileWorker.NextDelay(failure: null, Options)
            .Should().Be(TimeSpan.FromSeconds(30));

    [Fact]
    public void Quota_exhaustion_backs_off_for_the_configured_window()
        => ReconcileWorker.NextDelay(new EmbeddingQuotaExceededException("quota"), Options)
            .Should().Be(TimeSpan.FromSeconds(1800));

    [Fact]
    public void Other_failures_retry_on_the_normal_interval()
        => ReconcileWorker.NextDelay(new InvalidOperationException("db hiccup"), Options)
            .Should().Be(TimeSpan.FromSeconds(30));

    [Fact]
    public void Backoff_never_drops_below_one_second()
        => ReconcileWorker.NextDelay(
                new EmbeddingQuotaExceededException("quota"),
                new SearchIndexOptions { QuotaBackoffSeconds = 0 })
            .Should().Be(TimeSpan.FromSeconds(1));
}
