using System.Net.Http.Headers;

namespace EmployeeManager.Agents.Auth;

/// <summary>
/// Injects a fresh bearer token onto every outgoing MCP request. Sits on the HttpClient that
/// the MCP transport uses, so token refresh is transparent to the long-lived MCP client.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokens;

    public BearerTokenHandler(IAccessTokenProvider tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
