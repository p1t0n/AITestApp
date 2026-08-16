using Microsoft.Extensions.AI;

namespace CvManager.ToolSelectionEval;

/// <summary>
/// Runs the golden prompts against the real model with the REAL tool definitions (the live MCP
/// listing's AIFunctions, declaration-only — nothing is ever invoked) and records which tool the
/// model calls first. ToolMode stays Auto: the eval measures selection, not forcing.
/// </summary>
public static class ToolSelectionRunner
{
    private const string Instructions =
        "You are the assistant for a CV/roster manager. Tools are provided. Respond to the " +
        "user's request by calling the single most appropriate tool first. Always start with a " +
        "tool call; use exactly the ids and values the user gives you.";

    public static async Task<SelectionAggregate> RunAsync(
        IChatClient chat,
        IReadOnlyList<AIFunction> tools,
        IReadOnlyList<GoldenPrompt> prompts,
        TimeSpan pacing,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        // Declaration-only: even if a caller hands over invocable functions, the model's reply is
        // never executed (no UseFunctionInvocation in this pipeline) — but strip invocability
        // anyway so a bug can't call a real tool.
        var declarations = tools.Select(t => t.AsDeclarationOnly()).Cast<AITool>().ToList();
        var options = new ChatOptions { Tools = declarations, ToolMode = ChatToolMode.Auto };

        var results = new List<PromptResult>();
        foreach (var prompt in prompts)
        {
            // The production wire retries stochastic model faults (MALFORMED_FUNCTION_CALL and
            // friends) below the abstraction (GeminiCompatHandler); this runner sits on a plain
            // client, so it retries at the prompt level instead — the eval measures selection,
            // not transport luck. A prompt that still fails after the budget is a recorded miss.
            PromptResult result = null!;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var response = await chat.GetResponseAsync(
                        [new ChatMessage(ChatRole.System, Instructions), new ChatMessage(ChatRole.User, prompt.Text)],
                        options,
                        ct);
                    var calls = response.Messages
                        .SelectMany(m => m.Contents)
                        .OfType<FunctionCallContent>()
                        .Select(c => c.Name)
                        .ToList();
                    result = new PromptResult(prompt, calls.FirstOrDefault(), calls);
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new PromptResult(prompt, null, [], ex.Message);
                    if (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                    }
                }
            }

            results.Add(result);
            progress?.Invoke(Describe(result));

            if (!ReferenceEquals(prompt, prompts[^1]))
            {
                await Task.Delay(pacing, ct);
            }
        }

        return SelectionAggregate.From(results);
    }

    public static string Describe(PromptResult r) => r.Error is { } error
        ? $"{r.Prompt.Id,-26} ERROR: {error}"
        : $"{r.Prompt.Id,-26} expected={r.Prompt.ExpectedTool,-24} got={r.FirstCall ?? "(no call)",-24} {(r.FirstToolCorrect ? "ok" : "MISS")}";
}
