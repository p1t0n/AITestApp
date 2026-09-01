namespace ExpertToJob.Agents.Auth;

/// <summary>
/// Supplies a bearer token for calling the MCP server. The Roster Q&amp;A implementation uses
/// the OAuth 2.1 client-credentials grant against Keycloak and caches the token until it
/// nears expiry, so per-request calls are cheap.
/// </summary>
public interface IAccessTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct = default);
}
