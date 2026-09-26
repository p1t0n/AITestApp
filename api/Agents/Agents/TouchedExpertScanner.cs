using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExpertToJob.Agents.Agents;

/// <summary>
/// Reads the Experts a tool call touched out of the call itself — its arguments and its result —
/// in code (EXP-36, <c>manuals/adr-roster-qa-conversation-history.md</c> §3).
///
/// <para>The set this produces is what <c>ErasureService</c> later joins on to find the turns it
/// must scrub, so the bar is "never miss somebody the model could have quoted", not "name exactly
/// the people the answer mentions". A superset is the deliberate choice. What is <em>not</em>
/// allowed is the opposite error: collecting an id that is not a person, which would scrub a
/// stranger's turn on an unrelated erasure.</para>
///
/// <para><b>The model's own text is never an input.</b> An answer that quotes a GUID is prose; only
/// the structured result of a tool the guard actually invoked counts.</para>
/// </summary>
public static class TouchedExpertScanner
{
    /// <summary>Deep enough for the deepest read-tool payload nested inside an MCP envelope inside
    /// a content block; bounded so a cyclic or pathological result cannot spin.</summary>
    private const int MaxDepth = 16;

    /// <summary>
    /// Keys that only an Expert-shaped object carries. An <c>id</c> alone is not evidence of a
    /// person: a catalog skill, an experience row and an achievement bullet all have one, and
    /// <c>cv_get</c>'s payload is nothing but those. One of these alongside it is.
    /// </summary>
    private static readonly string[] ExpertMarkers =
        ["firstName", "lastName", "email", "currentCapacityPercent"];

    /// <summary>
    /// The distinct Expert ids this call touched. <paramref name="arguments"/> matters for exactly
    /// one reason and it is the important one: <c>cv_get</c> returns a CV, and a CV carries a full
    /// name and no id at all. The id it was <em>called</em> with is the only in-code evidence that
    /// this person's prose entered the prompt — and a CV turn is the one most worth scrubbing.
    /// </summary>
    public static IReadOnlyCollection<Guid> Scan(
        object? result, IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        var touched = new HashSet<Guid>();

        if (arguments is not null)
        {
            foreach (var (key, value) in arguments)
            {
                if (key.Equals("expertId", StringComparison.OrdinalIgnoreCase) && TryGuid(value, out var id))
                {
                    touched.Add(id);
                }
            }
        }

        try
        {
            Walk(JsonSerializer.SerializeToNode(result), touched, depth: 0);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A result that will not serialize names nobody. Losing the ids of a call that already
            // went wrong is not worth taking down an answer the user is waiting for.
        }

        return touched;
    }

    private static void Walk(JsonNode? node, HashSet<Guid> touched, int depth)
    {
        if (node is null || depth > MaxDepth)
        {
            return;
        }

        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item, touched, depth + 1);
                }

                break;

            case JsonObject obj:
                Collect(obj, touched);
                foreach (var (_, value) in obj)
                {
                    Walk(value, touched, depth + 1);
                }

                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                // A tool result usually arrives as JSON *text*: inside an MCP content block, or as
                // the single {"$type":"text","text":"{…}"} block the Agent Framework hands back.
                // Only text that is itself an object or an array is followed — a sentence is a
                // sentence, whatever GUIDs it happens to contain.
                if (Nested(text) is { } nested)
                {
                    Walk(nested, touched, depth + 1);
                }

                break;
        }
    }

    private static void Collect(JsonObject obj, HashSet<Guid> touched)
    {
        if (TryGuidProperty(obj, "expertId", out var explicitId))
        {
            touched.Add(explicitId);
            return;
        }

        if (TryGuidProperty(obj, "id", out var ownId) && LooksLikeAnExpert(obj))
        {
            touched.Add(ownId);
        }
    }

    private static bool LooksLikeAnExpert(JsonObject obj) =>
        obj.Any(p => ExpertMarkers.Contains(p.Key, StringComparer.OrdinalIgnoreCase));

    private static bool TryGuidProperty(JsonObject obj, string name, out Guid id)
    {
        foreach (var (key, value) in obj)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                && Guid.TryParse(text, out id))
            {
                return true;
            }
        }

        id = Guid.Empty;
        return false;
    }

    private static JsonNode? Nested(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGuid(object? value, out Guid id)
    {
        switch (value)
        {
            case Guid guid:
                id = guid;
                return true;
            case string text:
                return Guid.TryParse(text, out id);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return Guid.TryParse(element.GetString(), out id);
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var nodeText):
                return Guid.TryParse(nodeText, out id);
            default:
                id = Guid.Empty;
                return false;
        }
    }
}
