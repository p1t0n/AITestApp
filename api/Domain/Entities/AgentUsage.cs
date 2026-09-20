namespace ExpertToJob.Domain.Entities;

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

    /// <summary>Model that produced the response, e.g. "gemini-flash-lite-latest". Empty when the
    /// reply never reached a model — a measurement, never a label read back from configuration.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Chat backend that served the run, the <c>Ai:Chat:Provider</c> enum member's own name
    /// (e.g. "Gemini", "AzureFoundry"). Null on rows written before providers existed (EXP-19).
    /// <para>Recorded rather than derived: model ids already differ per provider, but they drift
    /// across aliases and version suffixes, so parsing one to recover the provider fails exactly
    /// when cost attribution starts to matter (ADR §2 decision 11). Write-only diagnostic data —
    /// nothing reads it, deliberately.</para></summary>
    public string? Provider { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }

    /// <summary>Summed model wall-clock time for the call, ms (null on legacy rows).</summary>
    public long? LatencyMs { get; set; }

    /// <summary>W3C trace id of the request that spent the tokens — clickable evidence in the
    /// Aspire dashboard (null on legacy rows or when tracing is off).</summary>
    public string? TraceId { get; set; }

    /// <summary>Model calls the run made — the Turn Amplification multiplier behind the token
    /// count. A tool-looping agent that answers in 10 iterations pays its prompt 10 times
    /// (null on rows written before P1T-144).</summary>
    public int? Iterations { get; set; }

    /// <summary>Ordered, comma-separated names of the tools the run called, e.g.
    /// "skill_list,cv_get,cv_get". The cheapest possible answer to "why was this call expensive"
    /// (null on legacy rows, and when the run called no tool).</summary>
    public string? ToolSequence { get; set; }

    /// <summary>Pipeline sub-step attribution (staffing: shortlist/match/narrative); null for
    /// direct agent calls.</summary>
    public string? Step { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
