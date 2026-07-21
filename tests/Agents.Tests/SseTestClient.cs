using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EmployeeManager.Agents.Tests;

/// <summary>One parsed SSE frame: the event name plus its data payload (JSON in this contract).</summary>
internal sealed record SseFrame(string Event, string Data)
{
    public JsonElement Json => JsonSerializer.Deserialize<JsonElement>(Data);
}

/// <summary>Minimal SSE consumption for the staffing endpoint tests: an unbuffered POST (so
/// frames can be read while the run is still in flight) and a line-level frame parser that
/// skips keep-alive comments.</summary>
internal static class SseTestClient
{
    public static Task<HttpResponseMessage> PostSseAsync(
        this HttpClient client, string url, object payload, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public static async Task<IReadOnlyList<SseFrame>> ReadAllSseFramesAsync(this HttpResponseMessage response)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var frames = new List<SseFrame>();
        while (await reader.ReadSseFrameAsync() is { } frame)
        {
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>Reads one SSE frame (event/data lines up to the blank separator), skipping
    /// comment (keep-alive) lines; null once the stream closes.</summary>
    public static async Task<SseFrame?> ReadSseFrameAsync(this StreamReader reader)
    {
        string? eventName = null;
        StringBuilder? data = null;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                if (eventName is not null || data is not null)
                {
                    return new SseFrame(eventName ?? "message", data?.ToString() ?? "");
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                (data ??= new StringBuilder()).Append(line["data: ".Length..]);
            }
        }

        return null;
    }
}
