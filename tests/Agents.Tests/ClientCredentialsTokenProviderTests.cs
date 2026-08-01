using CvManager.Agents.Auth;
using CvManager.Agents.Configuration;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;

namespace CvManager.Agents.Tests;

/// <summary>
/// Deterministic tests for the client-credentials token provider. A capturing HTTP handler stands
/// in for Keycloak, so these assert the provider posts the configured client's credentials and
/// scope to the realm token endpoint and returns the access token — no live Keycloak.
/// </summary>
public class ClientCredentialsTokenProviderTests
{
    [Fact]
    public async Task Posts_the_configured_clients_credentials_to_the_realm_token_endpoint()
    {
        var capture = new CapturingHandler(accessToken: "tok-123");
        var options = new McpClientAuthOptions
        {
            Authority = "http://localhost:8080/realms/cv-manager",
            ClientId = "agent-cv-tailoring",
            ClientSecret = "secret-xyz",
            Scope = "mcp:read",
        };
        var provider = new ClientCredentialsTokenProvider(
            new SingleHandlerHttpClientFactory(capture), options, TimeProvider.System);

        var token = await provider.GetTokenAsync();

        token.Should().Be("tok-123");
        capture.RequestUri.Should().Be("http://localhost:8080/realms/cv-manager/protocol/openid-connect/token");
        capture.Form["grant_type"].Should().Be("client_credentials");
        capture.Form["client_id"].Should().Be("agent-cv-tailoring");
        capture.Form["client_secret"].Should().Be("secret-xyz");
        capture.Form["scope"].Should().Be("mcp:read");
    }
}
