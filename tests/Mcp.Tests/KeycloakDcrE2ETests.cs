using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CvManager.Mcp.Tests;

/// <summary>
/// The Dynamic Client Registration ceiling (P1T-157) against a real Keycloak.
///
/// <para><see cref="KeycloakDcrPolicyTests"/> asserts what the realm <em>declares</em>; only a
/// running authorization server can show that it is <em>obeyed</em>. Every case here failed
/// differently against the previous export: registration used to hand back a client with
/// <c>fullScopeAllowed: true</c>, the password grant enabled and no PKCE attribute at all.</para>
///
/// <para>Requires Docker, so it sits behind <c>Category=e2e</c> with the rest.</para>
/// </summary>
[Trait("Category", "e2e")]
public class KeycloakDcrE2ETests : IAsyncLifetime
{
    private const string Admin = "admin";

#pragma warning disable CS0618 // parameterless ContainerBuilder is deprecated; the generic builder is the supported path for a plain image
    private readonly IContainer _keycloak = new ContainerBuilder()
#pragma warning restore CS0618
        .WithImage("quay.io/keycloak/keycloak:26.0")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", Admin)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", Admin)
        .WithResourceMapping(
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "realm-export.json")),
            "/opt/keycloak/data/import/")
        .WithCommand("start-dev", "--import-realm")
        .WithPortBinding(8080, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
            r.ForPort(8080).ForPath("/realms/cv-manager/.well-known/openid-configuration")))
        .Build();

    private readonly HttpClient _http = new();

    private string BaseUrl => $"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}";

    private string RegisterUrl => $"{BaseUrl}/realms/cv-manager/clients-registrations/default";

    public Task InitializeAsync() => _keycloak.StartAsync();

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _keycloak.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_registration_is_refused()
    {
        var response = await _http.PostAsync(
            $"{BaseUrl}/realms/cv-manager/clients-registrations/openid-connect",
            JsonContent.Create(new
            {
                client_name = "walk-in",
                redirect_uris = new[] { "http://localhost/callback" },
            }));

        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Trusted Hosts");
    }

    [Theory]
    [InlineData(McpScopes.Write)]
    [InlineData(McpScopes.Admin)]
    public async Task A_registering_client_cannot_ask_its_way_past_read(string scope)
    {
        var response = await RegisterAsync(new
        {
            clientId = $"escalate-{scope.Replace(':', '-')}",
            publicClient = false,
            serviceAccountsEnabled = true,
            defaultClientScopes = new[] { McpScopes.Read, scope },
        });

        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Allowed Client Scopes");
    }

    [Fact]
    public async Task A_registered_client_is_stamped_with_the_oauth_2_1_baseline_it_did_not_ask_for()
    {
        // Everything this registration requests is something the ceiling refuses to grant.
        var response = await RegisterAsync(new
        {
            clientId = "greedy-agent",
            secret = "greedy-secret",
            publicClient = false,
            serviceAccountsEnabled = true,
            standardFlowEnabled = true,
            implicitFlowEnabled = true,
            directAccessGrantsEnabled = true,
            fullScopeAllowed = true,
            redirectUris = new[] { "http://localhost/callback" },
            defaultClientScopes = new[] { McpScopes.Read, "mcp-audience" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var client = await GetClientAsync("greedy-agent");

        using var _ = new AssertionScope();
        client.GetProperty("fullScopeAllowed").GetBoolean().Should().BeFalse();
        client.GetProperty("implicitFlowEnabled").GetBoolean().Should().BeFalse();
        client.GetProperty("directAccessGrantsEnabled").GetBoolean().Should().BeFalse();
        client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString()
            .Should().Be("S256", "the profile stamps PKCE rather than trusting the registrar");
        client.GetProperty("defaultClientScopes").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo([McpScopes.Read, "mcp-audience"]);
    }

    [Fact]
    public async Task A_registered_client_gets_a_working_read_token_and_no_more()
    {
        (await RegisterAsync(new
        {
            clientId = "well-behaved-agent",
            secret = "well-behaved-secret",
            publicClient = false,
            serviceAccountsEnabled = true,
            defaultClientScopes = new[] { McpScopes.Read, "mcp-audience" },
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        using var _ = new AssertionScope();

        // Read works, and carries the audience the MCP server validates — DCR is a usable
        // onboarding path, not a decorative one.
        var granted = await TokenAsync("well-behaved-agent", "well-behaved-secret", McpScopes.Read);
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        var claims = ReadClaims(await granted.Content.ReadFromJsonAsync<JsonElement>());
        claims.GetProperty("scope").GetString().Should().Be(McpScopes.Read);
        claims.GetProperty("aud").GetRawText().Should().Contain("https://localhost/mcp",
            "the audience mapper rides on the mcp-audience scope; without it the MCP server "
            + "rejects the token whatever its scopes say");

        // Asking for write at the token endpoint fails too — the ceiling is on the client, so it
        // is not something the caller can route around by requesting a wider scope later.
        var refused = await TokenAsync(
            "well-behaved-agent", "well-behaved-secret", $"{McpScopes.Read} {McpScopes.Write}");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invalid_scope");
    }

    [Fact]
    public async Task The_realms_own_clients_still_authenticate_unchanged()
    {
        // The OAuth 2.1 profile is bound to the registration contexts only. If it ever reached
        // ByAuthenticatedUser or ran at runtime, secure-client-authenticator-style rules would
        // break every agent identity — this is the test that would catch it.
        var response = await TokenAsync(
            "cv-manager-e2e", "e2e-secret", $"{McpScopes.Read} {McpScopes.Write} {McpScopes.Admin}");

        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var claims = ReadClaims(await response.Content.ReadFromJsonAsync<JsonElement>());
        claims.GetProperty("scope").GetString()!.Split(' ')
            .Should().BeEquivalentTo([McpScopes.Read, McpScopes.Write, McpScopes.Admin]);
    }

    private async Task<HttpResponseMessage> RegisterAsync(object client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RegisterUrl)
        {
            Content = JsonContent.Create(client),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await InitialAccessTokenAsync());
        return await _http.SendAsync(request);
    }

    private Task<HttpResponseMessage> TokenAsync(string clientId, string secret, string scope) =>
        _http.PostAsync(
            $"{BaseUrl}/realms/cv-manager/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["scope"] = scope,
            }));

    private async Task<JsonElement> GetClientAsync(string clientId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/admin/realms/cv-manager/clients?clientId={clientId}");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
    }

    /// <summary>A short-lived admin token; the master-realm access token expires in a minute.</summary>
    private async Task<string> AdminTokenAsync()
    {
        var response = await _http.PostAsync(
            $"{BaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = Admin,
                ["password"] = Admin,
            }));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    private async Task<string> InitialAccessTokenAsync()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{BaseUrl}/admin/realms/cv-manager/clients-initial-access")
        {
            Content = JsonContent.Create(new { expiration = 0, count = 1 }),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    private static JsonElement ReadClaims(JsonElement tokenResponse)
    {
        var payload = tokenResponse.GetProperty("access_token").GetString()!.Split('.')[1];
        return JsonSerializer.Deserialize<JsonElement>(
            Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')
                .Replace('-', '+').Replace('_', '/')));
    }
}
