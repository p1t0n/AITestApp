using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// Gemini 3 models sign each function call and require the signature echoed on the next turn
/// (<c>tool_calls[n].extra_content.google.thought_signature</c>). The OpenAI SDK doesn't model
/// that field, so it is stripped from replayed history and Gemini rejects the request with 400
/// INVALID_ARGUMENT. This policy injects Google's documented bypass sentinel
/// (<c>skip_thought_signature_validator</c>) into any assistant tool call that lacks a signature —
/// the sanctioned escape hatch for clients that cannot round-trip the real one.
/// </summary>
public sealed class GeminiThoughtSignaturePolicy : PipelinePolicy
{
    private const string BypassSentinel = "skip_thought_signature_validator";

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        RewriteRequest(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        RewriteRequest(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void RewriteRequest(PipelineMessage message)
    {
        if (message.Request.Content is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        message.Request.Content.WriteTo(buffer);
        var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        if (!json.Contains("\"tool_calls\"", StringComparison.Ordinal))
        {
            return;
        }

        var rewritten = InjectSignatures(json);
        if (rewritten is not null)
        {
            message.Request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
        }
    }

    /// <summary>Pure transform, unit-tested directly: adds the sentinel to every assistant
    /// tool call missing a thought signature. Returns null when nothing needed changing.</summary>
    public static string? InjectSignatures(string requestJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(requestJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (root?["messages"] is not JsonArray messages)
        {
            return null;
        }

        var changed = false;
        foreach (var message in messages)
        {
            if ((string?)message?["role"] != "assistant" || message?["tool_calls"] is not JsonArray toolCalls)
            {
                continue;
            }

            foreach (var toolCall in toolCalls)
            {
                if (toolCall is null || toolCall["extra_content"]?["google"]?["thought_signature"] is not null)
                {
                    continue;
                }

                var extra = toolCall["extra_content"] as JsonObject ?? new JsonObject();
                var google = extra["google"] as JsonObject ?? new JsonObject();
                google["thought_signature"] = BypassSentinel;
                extra["google"] = google;
                toolCall["extra_content"] = extra;
                changed = true;
            }
        }

        return changed ? root.ToJsonString() : null;
    }
}
