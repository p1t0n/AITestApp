namespace ExpertToJob.Mcp;

/// <summary>The success payloads an MCP tool hands back when the operation itself returns no data.</summary>
internal static class McpToolResults
{
    /// <summary>
    /// What a void tool (every <c>*_delete</c>) returns: <c>{"ok":true}</c>. One instance rather
    /// than ten literals, because it is a wire contract an agent parses, not an implementation
    /// detail — <c>McpToolErrorResultFreezeTests</c> pins the serialized bytes.
    /// </summary>
    public static readonly object Ok = new { ok = true };
}
