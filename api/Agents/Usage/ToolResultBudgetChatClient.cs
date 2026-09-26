using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Usage;

/// <summary>
/// The Tool Result Budget seam (EXP-40): no single tool result reaches the model larger than the
/// agent's <see cref="AgentBudget.MaxToolResultTokens"/>. Design record:
/// <c>manuals/agent-cost-budgets.md</c> §8.
/// <para>
/// The Runtime Budget (<see cref="RuntimeBudgetChatClient"/>) checks what a run has spent BEFORE
/// each call, so it cannot stop the call that carries one oversized result: a 503-row
/// <c>expert_list</c> rode into roster-qa's fourth call whole, 54,669 tokens against a
/// 50,000-token day. This decorator looks at the payload itself instead.
/// </para>
/// <para>
/// It sits in the chat pipeline rather than on the tool source because code-driven agents
/// (bench-report, roster-scan) invoke the same MCP tools directly and need the full result —
/// only what the MODEL is shown is bounded. The result is swapped in the messages sent
/// downstream, never in the caller's list, so the same withholding repeats deterministically on
/// every later iteration of the loop.
/// </para>
/// </summary>
public sealed class ToolResultBudgetChatClient(
    IChatClient inner, string agentKey, AgentBudget budget, ILogger? logger = null)
    : DelegatingChatClient(inner)
{
    /// <summary>Characters per estimated token — the Cost Floors' yardstick (TokenEstimate), so
    /// a ceiling here reads in the same unit as the floors in <c>tools/CostFloors.Core</c>.</summary>
    private const double CharsPerToken = 4.0;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(Bound(messages), options, cancellationToken);

    private IEnumerable<ChatMessage> Bound(IEnumerable<ChatMessage> messages)
    {
        var list = messages.ToList();
        var toolNames = list
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .GroupBy(c => c.CallId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        for (var i = 0; i < list.Count; i++)
        {
            var message = list[i];
            if (!message.Contents.OfType<FunctionResultContent>().Any(r => Estimate(r.Result) > budget.MaxToolResultTokens))
            {
                continue;
            }

            var bounded = message.Clone();
            bounded.Contents = message.Contents
                .Select(c => c is FunctionResultContent r && Estimate(r.Result) is var tokens
                                                          && tokens > budget.MaxToolResultTokens
                    ? Withhold(r, toolNames.GetValueOrDefault(r.CallId) ?? "the tool", tokens)
                    : c)
                .ToList();
            list[i] = bounded;
        }

        return list;
    }

    private AIContent Withhold(FunctionResultContent result, string tool, long tokens)
    {
        // Invariant for the same reason as the Runtime Budget's notice (P1T-200): the model reads
        // this string, and a host's locale must not decide whether it says 5,000 or 5.000.
        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Tool Result Budget reached: {tool} returned ~{tokens:N0} estimated tokens, over this " +
            $"run's {budget.MaxToolResultTokens:N0}-token limit per tool result.");
        MeteringScope.ReportDegradation(reason);
        logger?.LogWarning("Agent {AgentKey}: {Reason} Result withheld from the model.", agentKey, reason);

        return new FunctionResultContent(
            result.CallId,
            reason + " The result was withheld. Do not repeat this call; narrow it with the " +
            "filters another tool offers (e.g. a filtered search) and answer from that instead.");
    }

    private static long Estimate(object? result)
    {
        var text = result switch
        {
            null => "",
            string s => s,
            _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions),
        };
        return (long)Math.Ceiling(text.Length / CharsPerToken);
    }
}
