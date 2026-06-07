using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace EmployeeManager.Mcp;

/// <summary>
/// Runs a tool body and translates the Application layer's domain exceptions into a
/// structured MCP tool error (via <see cref="McpToolErrorMapper"/>) so the calling agent
/// can read a machine code + per-field detail and self-correct. Non-domain exceptions
/// propagate unchanged.
/// </summary>
internal static class McpToolExecutor
{
    private static readonly JsonSerializerOptions ErrorJson = new(JsonSerializerDefaults.Web);

    public static async Task<object> RunAsync<T>(Func<Task<T>> body)
    {
        try
        {
            return (await body())!;
        }
        catch (Exception ex) when (McpToolErrorMapper.Map(ex) is { } error)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = JsonSerializer.Serialize(error, ErrorJson) },
                },
            };
        }
    }
}
