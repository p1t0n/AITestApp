using System.Text;
using System.Text.Json.Nodes;

namespace CvManager.Agents.Configuration;

/// <summary>
/// Response-side compatibility shim for Gemini's OpenAI-compatible endpoint. Gemini reports
/// nonstandard <c>finish_reason</c> values (e.g. <c>function_call_filter: MALFORMED_FUNCTION_CALL</c>)
/// that the OpenAI SDK's enum parser throws on. This handler (a) retries the request a couple of
/// times when the model produced a malformed function call — usually a sampling hiccup — and
/// (b) normalizes any finish_reason the SDK doesn't know to <c>"stop"</c> so the response parses
/// and the agent loop can degrade gracefully instead of crashing.
/// </summary>
public sealed class GeminiCompatHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;

    private static readonly string[] KnownFinishReasons =
        ["stop", "length", "tool_calls", "content_filter", "function_call"];

    public GeminiCompatHandler(HttpMessageHandler inner) : base(inner) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) != true)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Buffer the request body so it can be re-sent on retry.
        var requestBody = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType;

        for (var attempt = 1; ; attempt++)
        {
            if (requestBody is not null)
            {
                var content = new ByteArrayContent(requestBody);
                if (contentType is not null)
                {
                    content.Headers.ContentType = contentType;
                }

                request.Content = content;
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Contains("MALFORMED_FUNCTION_CALL", StringComparison.Ordinal) && attempt < MaxAttempts)
            {
                response.Dispose();
                continue; // sampling hiccup — ask again
            }

            var normalized = NormalizeFinishReasons(body);
            if (normalized is not null)
            {
                var replacement = new StringContent(normalized, Encoding.UTF8, "application/json");
                response.Content = replacement;
            }

            return response;
        }
    }

    /// <summary>Rewrites unknown choice finish_reason values to "stop". Returns null when the
    /// body needed no change (or isn't the JSON we expect).</summary>
    public static string? NormalizeFinishReasons(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (Exception)
        {
            return null;
        }

        if (root?["choices"] is not JsonArray choices)
        {
            return null;
        }

        var changed = false;
        foreach (var choice in choices)
        {
            if (choice?["finish_reason"] is JsonValue value
                && value.TryGetValue<string>(out var reason)
                && reason is not null
                && !KnownFinishReasons.Contains(reason))
            {
                choice["finish_reason"] = "stop";
                changed = true;
            }
        }

        return changed ? root.ToJsonString() : null;
    }
}
