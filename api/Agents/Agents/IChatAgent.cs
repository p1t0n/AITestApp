namespace ExpertToJob.Agents.Agents;

/// <summary>
/// A conversational agent over ExpertToJob. This is the extension seam: future agents
/// (CV Tailoring, Resume Ingestion, Staffing/Match) implement the same shape and get their own
/// endpoint. Issue #15 ships a single-turn Roster Q&amp;A; threaded sessions arrive in #16.
/// </summary>
public interface IChatAgent
{
    /// <summary>Stable name, also used for routing / logging.</summary>
    string Name { get; }

    /// <summary>Answer one question. Single-turn for now (no conversation memory).</summary>
    Task<AgentReply> AskAsync(string question, CancellationToken ct = default);
}

/// <summary>An agent's answer plus the token usage the model reported for the call.
/// <see cref="ModelId"/> and <see cref="LatencyMs"/> arrive from the metering seam (P1T-95):
/// the real response model id and the summed model wall-clock time across the run's calls.
/// <see cref="Iterations"/> and <see cref="ToolSequence"/> arrive from the same seam (P1T-144)
/// and say WHY a run cost what it did — how many model calls it took and which tools it called,
/// in order. <see cref="Degradation"/> arrives from the Runtime Budget (P1T-147) and states, when
/// set, that the run was cut short of the tool calls it wanted — absence stated, never papered
/// over. Prose answers also carry the note in <see cref="Text"/>; schema-constrained ones cannot,
/// so this field is the only record they have.
/// <see cref="Grounded"/> and <see cref="TouchedExpertIds"/> arrive from the Capture-Verify scope
/// (EXP-36) and are what a stored Roster Q&amp;A turn is written from: whether the answer rested on
/// a tool result, and every Expert whose id appeared in one. Both are read in code — the dock never
/// parses the ungrounded note out of the text, and erasure never parses names out of the
/// answer.</summary>
public sealed record AgentReply(
    string Text,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    string? ModelId = null,
    long LatencyMs = 0,
    int Iterations = 0,
    string? ToolSequence = null,
    string? Degradation = null,
    bool Grounded = false,
    IReadOnlyList<Guid>? TouchedExpertIds = null)
{
    /// <summary>Every Expert the run's tool calls touched — empty rather than null for an agent
    /// that captures none, so no caller has to ask which of the two it got.</summary>
    public IReadOnlyList<Guid> TouchedExpertIds { get; init; } = TouchedExpertIds ?? [];
}
