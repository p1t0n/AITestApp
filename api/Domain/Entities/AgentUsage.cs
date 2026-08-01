namespace CvManager.Domain.Entities;

/// <summary>
/// One row per AI agent call: which user spent how many tokens on which agent/model and when.
/// Append-only usage log; per-user daily/weekly/monthly caps are computed by aggregating these
/// over UTC calendar windows. Keyed by <see cref="UserId"/> (cascade-deleted with the user).
/// </summary>
public class AgentUsage
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Agent that served the call, e.g. "roster-qa".</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Model that produced the response, e.g. "gemini-flash-lite-latest".</summary>
    public string Model { get; set; } = string.Empty;

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }

    /// <summary>Summed model wall-clock time for the call, ms (null on legacy rows).</summary>
    public long? LatencyMs { get; set; }

    /// <summary>W3C trace id of the request that spent the tokens — clickable evidence in the
    /// Aspire dashboard (null on legacy rows or when tracing is off).</summary>
    public string? TraceId { get; set; }

    /// <summary>Pipeline sub-step attribution (staffing: shortlist/match/narrative); null for
    /// direct agent calls.</summary>
    public string? Step { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
