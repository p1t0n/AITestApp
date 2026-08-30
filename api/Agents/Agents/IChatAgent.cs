namespace CvManager.Agents.Agents;

/// <summary>
/// A conversational agent over the CV Manager. This is the extension seam: future agents
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
/// so this field is the only record they have.</summary>
public sealed record AgentReply(
    string Text,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    string? ModelId = null,
    long LatencyMs = 0,
    int Iterations = 0,
    string? ToolSequence = null,
    string? Degradation = null);
