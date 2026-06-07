namespace EmployeeManager.Mcp;

/// <summary>
/// Static bearer/API-key gate for the MCP endpoint. The expected token is read from
/// configuration key <c>Mcp:ApiKey</c>; a request without a matching
/// <c>Authorization: Bearer &lt;token&gt;</c> header is rejected with 401.
/// This is the POC gate — OAuth 2.1 is deferred (see PRD).
/// </summary>
public static class McpBearerAuth
{
    private const string Scheme = "Bearer ";

    public static IApplicationBuilder UseMcpBearerAuth(this IApplicationBuilder app)
    {
        var expected = app.ApplicationServices.GetRequiredService<IConfiguration>()["Mcp:ApiKey"];

        return app.Use(async (context, next) =>
        {
            if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next();
        });
    }

    internal static bool IsAuthorized(string authorizationHeader, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return false; // no key configured → deny by default
        if (!authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        var token = authorizationHeader[Scheme.Length..].Trim();
        return token == expected;
    }
}
