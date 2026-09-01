using System.ClientModel;
using Microsoft.Extensions.AI;

namespace ExpertToJob.ToolSelectionEval;

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
        // Pinned per P1T-138: run-to-run first-tool accuracy on this model moved ±2 prompts at
        // default temperature — larger than either description pass's aggregate gain. Seed is
        // NOT set: the Gemini OpenAI-compat endpoint rejects an unrecognized "seed" field with a
        // hard 400 (verified directly against the endpoint), so Temperature = 0 alone is what's
        // available here to cut variance.
        var options = new ChatOptions
        {
            Tools = declarations,
            ToolMode = ChatToolMode.Auto,
            Temperature = 0f,
        };

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
                    result = new PromptResult(prompt, null, [], DescribeFault(ex));
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

    /// <summary>
    /// The exception message alone is useless for diagnosis: the OpenAI-compat client surfaces
    /// every HTTP fault as "Service request failed.", so a quota-exhausted run and a genuine
    /// selection collapse render identically in the report — two runs on 2026-08-29 had to be
    /// thrown away before anyone could say which they were. Pull out the status code and the
    /// service's own message so a 429 reads as a 429.
    /// </summary>
    public static string DescribeFault(Exception ex)
    {
        if (ex is not ClientResultException { Status: > 0 } failure) return ex.Message;

        var body = TryReadBody(failure);
        return body is null ? $"HTTP {failure.Status}" : $"HTTP {failure.Status}: {body}";
    }

    private static string? TryReadBody(ClientResultException failure)
    {
        string raw;
        try
        {
            raw = failure.GetRawResponse()?.Content?.ToString() ?? "";
        }
        catch (Exception)
        {
            // A response whose body was never buffered — nothing to add beyond the status.
            return null;
        }

        var flat = string.Join(" ", raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0) return null;
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }

    public static string Describe(PromptResult r) => r.Error is { } error
        ? $"{r.Prompt.Id,-26} ERROR: {error}"
        : $"{r.Prompt.Id,-26} expected={r.Prompt.ExpectedTool,-24} got={r.FirstCall ?? "(no call)",-24} {(r.FirstToolCorrect ? "ok" : "MISS")}";
}
