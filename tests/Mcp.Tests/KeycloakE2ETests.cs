using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Xunit;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// End-to-end: a real Keycloak (imported realm) issues a token; the MCP server validates it
/// against Keycloak's JWKS (issuer + audience + signature + scopes) and authorizes tool access.
/// Proves the Resource-Server &lt;-&gt; Authorization-Server trust chain that the hermetic tests stub.
/// Requires Docker; the interactive PKCE redirect itself is enforced by realm config and not
/// exercised here (a confidential client-credentials grant stands in to obtain a scoped token).
/// </summary>
[Trait("Category", "e2e")]
public class KeycloakE2ETests : IAsyncLifetime
{
    private readonly IContainer _keycloak = new ContainerBuilder()
        .WithImage("quay.io/keycloak/keycloak:26.0")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithResourceMapping(
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "realm-export.json")),
            "/opt/keycloak/data/import/")
        .WithCommand("start-dev", "--import-realm")
        .WithPortBinding(8080, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
            r.ForPort(8080).ForPath("/realms/cv-manager/.well-known/openid-configuration")))
        .Build();

    public Task InitializeAsync() => _keycloak.StartAsync();

    public Task DisposeAsync() => _keycloak.DisposeAsync().AsTask();

    [Fact]
    public async Task Real_keycloak_token_is_validated_and_scope_gated()
    {
        var authority = $"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}/realms/cv-manager";
        var token = await GetClientCredentialsTokenAsync(authority);

        using var factory = McpTestHost.CreateFactoryWithAuthority(
            nameof(Real_keycloak_token_is_validated_and_scope_gated), authority, McpTestHost.Resource);
        McpTestHost.SeedEmployee(factory);
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        names.Should().Contain("employee_list");
        names.Should().Contain("employee_delete"); // e2e client carries mcp:admin

        var list = await client.CallToolAsync("employee_list");
        McpTestHost.Text(list).Should().Contain("Lovelace");
    }

    private static async Task<string> GetClientCredentialsTokenAsync(string authority)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "cv-manager-e2e",
                ["client_secret"] = "e2e-secret",
                ["scope"] = "mcp:read mcp:write mcp:admin",
            }));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }
}
