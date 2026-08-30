using CvManager.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

/// <summary>
/// Read-only conversational agent over the employee roster. A Microsoft Agent Framework
/// <see cref="ChatClientAgent"/> backed by the configured chat model, given the MCP read tools.
/// It answers natural-language questions ("who knows React?") by calling those tools and never
/// fabricates data. Single-turn: each question runs on a fresh, ephemeral session.
/// </summary>
public sealed class RosterQaAgent : IChatAgent
{
    private const string Instructions =
        """
        You are the Roster Q&A assistant for a CV Manager. Answer questions about the roster of
        employees — skills, qualifications, experience, languages, availability — using ONLY the
        provided tools. Never invent employees, skills, or facts.

        Converge: aim to answer in two tool calls, never exceed four. Before each call, ask what it
        adds that you lack; once a result answers the question, answer and stop.

        Choosing a tool:
        - Capability / experience questions — "who has done X", "anyone with a Z background" — live in
          employees' free-text career history: use roster_semantic_search and quote the evidence
          snippets it returns.
        - Put the WHOLE question into that one call. Its filters (location, skillIds, availableOn,
          minYears) are how constraints combine; look a skill id up with skill_list first when you need
          skillIds. Never rebuild a filter by hand from employee_list plus per-person cv_get, and never
          re-run a search reworded — one filtered result set is the answer, and an empty one means
          nobody matches, which is also an answer.
        - For exact facts — a skill level, availability on a date, languages, contact details — use the
          list/get tools. cv_get is for one person you already identified, never for scanning.
        - If roster_semantic_search errors or finds nothing, say so, then fall back to those tools.
        - A degradedReason means the matches are real but keyword-ranked — use them, and say ranking
          quality is reduced.

        Name each employee in full with their id in parentheses, e.g. "Ada Lovelace (a1b2c3d4-...)".

        If the tools return nothing relevant, say so plainly. You have read-only access; if asked to
        change data, explain that you cannot.
        """;

    private readonly IChatClient _chatClient;
    private readonly IMcpToolSource _toolSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;
    private bool _hasTools;

    public RosterQaAgent(IChatClient chatClient, IMcpToolSource toolSource, ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolSource = toolSource;
        _loggerFactory = loggerFactory;
    }

    public string Name => "roster-qa";

    /// <summary>The Capture-Verify Guard's hardened retry instruction (P1T-130): appended as an
    /// extra user message when the first run answered without any tool result behind it.</summary>
    private const string GroundingRetryInstruction =
        "IMPORTANT: You must ground your answer in a tool result. Call one of the provided " +
        "roster tools first, then answer strictly from its output.";

    /// <summary>Appended to the answer when even the retry produced no tool-backed evidence —
    /// an answer-level degrade, never an error (P1T-130).</summary>
    private const string UngroundedNote =
        "\n\n_Note: this answer could not be grounded in roster data._";

    public Task<AgentReply> AskAsync(string question, CancellationToken ct = default)
        => AskAsync(question, [], ct);

    /// <summary>Answers one question with prior turns replayed ahead of it (threaded sessions,
    /// P1T-93). The session itself stays ephemeral — the bounded history IS the memory, so the
    /// prompt size is controlled by the thread store, not by session growth.</summary>
    public async Task<AgentReply> AskAsync(
        string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        var agent = await GetAgentAsync(ct);
        var messages = history.Append(new ChatMessage(ChatRole.User, question)).ToList();
        using var metering = Usage.MeteringScope.Begin();
        using var capture = CaptureScope.Begin();

        // Force grounding on the first model call (P1T-130): RequireAny is one-shot by design —
        // FunctionInvokingChatClient resets the tool mode after the first iteration, so the model
        // must start from a tool but stays free to answer once results are in hand. RequireAny
        // over RequireSpecific: exact-fact questions legitimately ground through employee_list
        // and friends, not just roster_semantic_search — force grounding, not one tool. Applied
        // on every turn of a thread too (no opt-out flag): each answer must stand on fresh roster
        // data, and a follow-up re-query is cheap.
        // A tool-less agent (possible only in tests) has nothing to force and nothing to verify.
        var options = _hasTools
            ? new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { ToolMode = ChatToolMode.RequireAny },
            }
            : null;

        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(messages, session, options, ct);
        long inputTokens = response.Usage?.InputTokenCount ?? 0;
        long outputTokens = response.Usage?.OutputTokenCount ?? 0;
        long totalTokens = response.Usage?.TotalTokenCount ?? 0;
        var text = response.Text;

        // Capture-Verify: an answer with no captured tool result behind it gets ONE retry with a
        // hardened instruction; a second ungrounded answer ships with an explicit degrade note.
        // Tokens from both attempts are real and both are reported (the caller meters the total).
        if (_hasTools && !capture.Captured)
        {
            var retrySession = await agent.CreateSessionAsync(ct);
            var retryMessages = messages
                .Append(new ChatMessage(ChatRole.User, GroundingRetryInstruction))
                .ToList();
            var retry = await agent.RunAsync(retryMessages, retrySession, options, ct);
            inputTokens += retry.Usage?.InputTokenCount ?? 0;
            outputTokens += retry.Usage?.OutputTokenCount ?? 0;
            totalTokens += retry.Usage?.TotalTokenCount ?? 0;
            text = capture.Captured ? retry.Text : retry.Text + UngroundedNote;
        }

        var run = metering.Snapshot();
        return new AgentReply(
            text, inputTokens, outputTokens, totalTokens,
            run.ModelId, run.LatencyMs, run.Iterations, run.ToolSequence, run.Degradation);
    }

    private async Task<AIAgent> GetAgentAsync(CancellationToken ct)
    {
        if (_agent is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_agent is { } stillCached)
            {
                return stillCached;
            }

            // Wrapped so every successful invocation reports into the run's CaptureScope —
            // the Capture-Verify Guard's evidence that an answer has real data behind it.
            var tools = CaptureVerifyGuard.WrapTools(await _toolSource.GetToolsAsync(ct));
            _hasTools = tools.Count > 0;
            _agent = new ChatClientAgent(
                _chatClient,
                instructions: Instructions,
                name: "RosterQa",
                description: "Answers read-only questions about the employee roster.",
                tools: tools,
                loggerFactory: _loggerFactory);
            return _agent;
        }
        finally
        {
            _gate.Release();
        }
    }
}
