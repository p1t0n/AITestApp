using System.Net.Http.Json;
using System.Text.Json;
using CvManager.Agents.Configuration;

namespace CvManager.Agents.Auth;

/// <summary>
/// Obtains a scoped JWT from Keycloak via the OAuth 2.1 client-credentials grant and caches it
/// until shortly before it expires. The token carries only the configured scope (e.g.
/// <c>mcp:read</c>), so the agent is structurally limited to the matching MCP tools.
/// </summary>
public sealed class ClientCredentialsTokenProvider : IAccessTokenProvider
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpClientAuthOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ClientCredentialsTokenProvider(
        IHttpClientFactory httpClientFactory,
        McpClientAuthOptions options,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _time = time;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (_cachedToken is { } cached && _time.GetUtcNow() < _expiresAt)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedToken is { } stillValid && _time.GetUtcNow() < _expiresAt)
            {
                return stillValid;
            }

            return await FetchTokenAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> FetchTokenAsync(CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient(nameof(ClientCredentialsTokenProvider));
        var tokenEndpoint = $"{_options.Authority.TrimEnd('/')}/protocol/openid-connect/token";

        using var response = await http.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope,
            }),
            ct);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var token = payload.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("Keycloak token response had no access_token.");
        var expiresIn = payload.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;

        _cachedToken = token;
        _expiresAt = _time.GetUtcNow().AddSeconds(expiresIn) - ExpirySkew;
        return token;
    }
}
