using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvManager.Agents.Agents;

/// <summary>
/// Lenient extraction of a typed payload from whatever shape an MCP tool result arrives in:
/// a POCO/JsonElement of the payload itself, a JSON string of it, a single
/// <c>Microsoft.Extensions.AI.TextContent</c> block (how the Agent Framework hands back an MCP
/// tool result — it serializes to <c>{"$type":"text","text":"{…payload…}"}</c>; missing that shape
/// was a production bug in the shortlist flow), or an MCP CallToolResult envelope whose
/// structuredContent / text content blocks hold the payload JSON.
/// </summary>
internal static class ToolResultPayload
{
    public static T? Extract<T>(object? result, Func<JsonObject, bool> looksLikePayload, JsonSerializerOptions json)
        where T : class
    {
        try
        {
            return FromNode<T>(JsonSerializer.SerializeToNode(result, json), looksLikePayload, json, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? FromNode<T>(
        JsonNode? node, Func<JsonObject, bool> looksLikePayload, JsonSerializerOptions json, int depth)
        where T : class
    {
        if (node is null || depth > 3)
        {
            return null;
        }

        // A JSON string: parse its content and recurse (tools often return serialized JSON text).
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            try
            {
                return FromNode<T>(JsonNode.Parse(text), looksLikePayload, json, depth + 1);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        if (looksLikePayload(obj))
        {
            return obj.Deserialize<T>(json);
        }

        // MCP envelope: prefer structuredContent, else scan text content blocks.
        if (obj["structuredContent"] is { } structured
            && FromNode<T>(structured, looksLikePayload, json, depth + 1) is { } fromStructured)
        {
            return fromStructured;
        }

        // A single AIContent text block ({"$type":"text","text":"{…payload…}"}).
        if (obj["text"] is JsonValue single
            && single.TryGetValue<string>(out var singleText)
            && FromNode<T>(JsonValue.Create(singleText), looksLikePayload, json, depth + 1) is { } fromSingle)
        {
            return fromSingle;
        }

        if (obj["content"] is JsonArray content)
        {
            foreach (var block in content)
            {
                if (block?["text"] is JsonValue textValue
                    && textValue.TryGetValue<string>(out var blockText)
                    && FromNode<T>(JsonValue.Create(blockText), looksLikePayload, json, depth + 1) is { } payload)
                {
                    return payload;
                }
            }
        }

        return null;
    }
}
