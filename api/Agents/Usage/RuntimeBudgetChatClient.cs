using Microsoft.Extensions.AI;

namespace CvManager.Agents.Usage;

/// <summary>
/// The Runtime Budget seam (P1T-147): bounds what one agent run may spend, and degrades instead of
/// truncating. Design record: <c>manuals/agent-cost-budgets.md</c> §3.1–3.2.
/// <para>
/// It sits INSIDE the function-invocation loop — the same seam <see cref="MeteringChatClient"/>
/// occupies — so it observes every iteration rather than only the run's first and last call.
/// Before each model call it reads what the run has already spent off the ambient
/// <see cref="MeteringScope"/>; once either ceiling is reached it clones the
/// <see cref="ChatOptions"/> with <see cref="ChatToolMode.None"/> and appends an instruction to
/// answer from what is already in hand. The model then writes a real closing answer carrying a
/// Degradation note — never a 500, and never a dangling tool call.
/// </para>
/// <para>
/// Rejected: aborting the run and salvaging the last assistant text. It throws away work already
/// paid for and produces a worse answer than the model would write itself.
/// </para>
/// <para>
/// Both ceilings live here rather than one of them on
/// <c>FunctionInvokingChatClient.MaximumIterationsPerRequest</c>: reaching that limit stops the
/// loop mid-flight and can hand back an unanswered tool call, and reaching it at all requires
/// <c>ChatClientAgentOptions.UseProvidedChatClientAsIs</c>, which drops MAF's internal decorators
/// (function-approval bypass, context providers, message injection) that we cannot reconstruct
/// because their types are internal. Same ceiling, graceful exit, no forked pipeline.
/// </para>
/// </summary>
public sealed class RuntimeBudgetChatClient(
    IChatClient inner, string agentKey, AgentBudget budget, ILogger? logger = null)
    : DelegatingChatClient(inner)
{
    /// <summary>The closing turn appended once the budget is spent. Phrased as an instruction to
    /// answer, not to apologise: the model has real tool results in hand and should use them.</summary>
    public const string ClosingInstruction =
        "BUDGET REACHED: no further tool calls are available for this question. Answer now, " +
        "strictly from the tool results already in this conversation. If they do not cover the " +
        "question, say plainly what is missing rather than guessing.";

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var reason = OverBudget(options);
        if (reason is null)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }

        // Clone before mutating: ChatOptions is the caller's instance and is reused across the
        // loop's iterations, so flipping it in place would silently disarm the agent's own wiring
        // (roster-qa's first-call tool forcing, for one) well past this call.
        var closingOptions = options!.Clone();
        closingOptions.ToolMode = ChatToolMode.None;
        var closingMessages = messages.Append(new ChatMessage(ChatRole.User, ClosingInstruction));

        MeteringScope.ReportDegradation(reason);
        logger?.LogWarning(
            "Agent {AgentKey} reached its Runtime Budget ({Budget} input tokens / {Iterations} " +
            "model calls); withdrawing tools and asking for a closing answer. Reason: {Reason}",
            agentKey, budget.MaxInputTokens, budget.MaxIterations, reason);

        var response = await base.GetResponseAsync(closingMessages, closingOptions, cancellationToken);
        AppendDegradationNote(response, reason, options);
        return response;
    }

    /// <summary>The reason this call must be the closing one, or null to let it run untouched.
    /// Returns null when there is nothing to withdraw — no tools on offer, or tools already off —
    /// so an already-closing call is not re-instructed on every subsequent iteration.</summary>
    private string? OverBudget(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } || options.ToolMode is NoneChatToolMode)
        {
            return null;
        }

        if (MeteringScope.Spend() is not { } spend)
        {
            return null;
        }

        if (spend.InputTokens >= budget.MaxInputTokens)
        {
            return $"Runtime Budget reached: {spend.InputTokens:N0} of {budget.MaxInputTokens:N0} " +
                   $"input tokens spent over {spend.Iterations} model calls.";
        }

        if (spend.Iterations >= budget.MaxIterations)
        {
            return $"Runtime Budget reached: {spend.Iterations} of {budget.MaxIterations} model " +
                   $"calls made ({spend.InputTokens:N0} input tokens).";
        }

        return null;
    }

    /// <summary>
    /// Makes the Degradation visible in the answer itself, for the agents whose answer IS prose.
    /// Skipped when the run asked for a JSON schema (resume-ingestion, match): appending prose to
    /// a schema-constrained response would break the caller's parse, and those runs read the same
    /// Degradation off <see cref="MeteringSnapshot.Degradation"/> instead.
    /// </summary>
    private static void AppendDegradationNote(ChatResponse response, string reason, ChatOptions? options)
    {
        if (options?.ResponseFormat is ChatResponseFormatJson)
        {
            return;
        }

        var last = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)
                   ?? response.Messages.LastOrDefault();
        last?.Contents.Add(new TextContent($"\n\n_Note: {reason} This answer was composed from the " +
                                           "evidence already gathered, without further tool calls._"));
    }

    /// <summary>The wrapped client is a DI singleton shared by every agent on the same model, and
    /// one wrapper is built per agent — so this decorator owns its budget, not the pipeline under
    /// it, and must not dispose it out from under its siblings.</summary>
    protected override void Dispose(bool disposing)
    {
    }
}
