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

/// <summary>An agent's answer plus the token usage the model reported for the call.</summary>
public sealed record AgentReply(string Text, long InputTokens, long OutputTokens, long TotalTokens);
