using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using ExpertToJob.KeycloakRealm;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Builds <c>keycloak/Dockerfile</c> and starts it once for the whole class, with the eight agent
/// secrets in the environment.
///
/// <para>A fixture rather than <see cref="IAsyncLifetime"/> on the test class itself: xUnit
/// constructs a test class per test, so the shape the older Keycloak suites use would start five
/// servers to ask five read-only questions of the same realm.</para>
/// </summary>
public sealed class ProdKeycloakImage : IAsyncLifetime
{
    /// <summary>The value this run's environment supplies for <c>agent-roster-qa</c>.</summary>
    public const string RosterQaSecret = "a-secret-that-is-not-in-the-repository";

    /// <summary>What <c>keycloak/realm-export.json</c> still carries for the same client.</summary>
    public const string DevRosterQaSecret = "agent-roster-qa-secret";

    /// <summary>
    /// Deliberately not the address the test reaches the server on: the issuer in the minted token
    /// is then evidence that the image takes its hostname from the environment rather than from
    /// whatever address the request happened to arrive at.
    /// </summary>
    public const string Hostname = "https://auth.example.test";

    private readonly IFutureDockerImage _image = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(Path.Combine(RepoRoot(), "keycloak"))
        .WithDockerfile("Dockerfile")
        .WithName("experttojob-keycloak:tests")
        .WithDeleteIfExists(true)
        .WithCleanUp(false)
        .Build();

    private readonly IContainer _keycloak;

    public ProdKeycloakImage()
    {
#pragma warning disable CS0618 // parameterless ContainerBuilder is deprecated; the generic builder is the supported path for a plain image
        var container = new ContainerBuilder()
#pragma warning restore CS0618
            .WithImage(_image)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithEnvironment("KC_HOSTNAME", Hostname)
            // dev-file rather than a second container: see the class comment on the test class.
            .WithEnvironment("KC_DB", "dev-file")
            .WithPortBinding(8080, true)
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(8080).ForPath("/realms/expert-to-job/.well-known/openid-configuration")));

        // Every agent client, not just the one asserted on: an unset variable is left as the
        // literal `${...}` text rather than failing the import, so a run that supplied only one
        // would quietly leave seven clients holding a string this repository publishes.
        foreach (var clientId in AgentClientIds())
        {
            container = container.WithEnvironment(
                ProdRealmTransform.SecretEnvVar(clientId), EnvSecretFor(clientId));
        }

        _keycloak = container.Build();
    }

    public string BaseUrl => $"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}";

    public string ManagementUrl => $"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(9000)}";

    public async Task InitializeAsync()
    {
        await _image.CreateAsync();
        await _keycloak.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _keycloak.DisposeAsync();
        await _image.DisposeAsync();
    }

    /// <summary>The secret this run put in the environment for <paramref name="clientId"/>.</summary>
    public static string EnvSecretFor(string clientId) =>
        clientId == "agent-roster-qa" ? RosterQaSecret : $"{clientId}-env-value";

    /// <summary>Read from the generated realm, so the list cannot drift from what ships.</summary>
    public static IReadOnlyList<string> AgentClientIds() =>
        JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, ProdRealmTransform.ProdFileName)))
            .RootElement.GetProperty("clients").EnumerateArray()
            .Select(c => c.GetProperty("clientId").GetString()!)
            .Where(id => id.StartsWith("agent-", StringComparison.Ordinal))
            .ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; keycloak/Dockerfile cannot be built.");
    }
}

/// <summary>
/// The production Keycloak image (EXP-117) against a real server.
///
/// <para><see cref="ProdRealmTransformTests"/> asserts what the generated realm <em>says</em>.
/// This asserts that Keycloak 26 <em>does</em> it: that a bare <c>${AGENT_ROSTER_QA_SECRET}</c>
/// in an import file is resolved from the environment at all (the <c>${env.NAME}</c> spelling is
/// silently left as a literal, which would ship a public-repository string as a client secret),
/// that the committed dev secret no longer opens the door, and that the <c>mcp-audience</c>
/// mapper still stamps the audience the MCP resource server validates even though the client
/// often assumed to own it is gone.</para>
///
/// <para><strong>Why dev-file and not a Postgres container.</strong> Nothing asserted here touches
/// the database: placeholder substitution happens on the file before import, and a
/// client-credentials grant reads the same imported client record whatever the JDBC driver
/// underneath is. A second container would add startup time and a second failure mode to a test
/// that measures neither. The Postgres path is exercised by the deployment itself (EXP-121) and
/// by the migrator suites.</para>
///
/// <para><strong>Why <c>Category=e2e</c>.</strong> It needs Docker and it builds an image, which
/// is exactly what the local loop's <c>Category!=e2e&amp;Category!=live</c> filter exists to keep
/// out. CI runs <c>dotnet test</c> with no filter (<c>.github/workflows/ci.yml</c>, "Build &amp;
/// Test"), so it runs there beside <see cref="KeycloakE2ETests"/> and
/// <see cref="KeycloakDcrE2ETests"/> — no new category, no workflow change.</para>
/// </summary>
[Trait("Category", "e2e")]
public class KeycloakProdRealmImageE2ETests : IClassFixture<ProdKeycloakImage>
{
    private readonly ProdKeycloakImage _keycloak;
    private readonly HttpClient _http = new();

    public KeycloakProdRealmImageE2ETests(ProdKeycloakImage keycloak) => _keycloak = keycloak;

    private string TokenUrl =>
        $"{_keycloak.BaseUrl}/realms/expert-to-job/protocol/openid-connect/token";

    [Fact]
    public async Task The_environment_supplies_the_agent_secret_and_the_dev_constant_no_longer_works()
    {
        var withEnvSecret = await TokenAsync("agent-roster-qa", ProdKeycloakImage.RosterQaSecret);
        var withDevSecret = await TokenAsync("agent-roster-qa", ProdKeycloakImage.DevRosterQaSecret);
        var withLiteralPlaceholder = await TokenAsync(
            "agent-roster-qa", "${" + ProdRealmTransform.SecretEnvVar("agent-roster-qa") + "}");

        using var _ = new AssertionScope();
        withEnvSecret.StatusCode.Should().Be(HttpStatusCode.OK,
            "Keycloak 26 resolves a bare ${NAME} placeholder in an import file from the environment");
        withDevSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the committed dev secret must not open a production client");
        withLiteralPlaceholder.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a resolved placeholder leaves no literal behind for anyone reading this repository");
    }

    [Fact]
    public async Task Every_agent_client_authenticates_with_its_own_environment_secret()
    {
        var clientIds = ProdKeycloakImage.AgentClientIds();
        clientIds.Should().HaveCount(8);

        using var _ = new AssertionScope();
        foreach (var clientId in clientIds)
        {
            var response = await TokenAsync(clientId, ProdKeycloakImage.EnvSecretFor(clientId));
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{clientId} must still be usable");
        }
    }

    [Fact]
    public async Task The_token_still_carries_the_audience_the_mcp_server_validates()
    {
        // The reason it survives removing `expert-to-job-mcp`: the mapper is on the shared
        // `mcp-audience` client scope, which every agent client keeps.
        var response = await TokenAsync("agent-roster-qa", ProdKeycloakImage.RosterQaSecret);
        response.EnsureSuccessStatusCode();

        var claims = await ClaimsAsync(response);

        using var _ = new AssertionScope();
        Audiences(claims).Should().Contain(McpTestHost.Resource);
        claims.GetProperty("iss").GetString().Should()
            .Be($"{ProdKeycloakImage.Hostname}/realms/expert-to-job",
                "the hostname comes from KC_HOSTNAME, not from the request");
        claims.GetProperty("scope").GetString().Should().Contain("mcp:read");
    }

    [Fact]
    public async Task The_dev_only_clients_are_not_in_the_deployed_realm()
    {
        using var _ = new AssertionScope();
        (await TokenAsync("expert-to-job-e2e", "e2e-secret")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await TokenAsync("expert-to-job-mcp", "whatever")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_management_health_endpoint_is_enabled()
    {
        // --health-enabled=true in the image's CMD, and on 9000 rather than 8080 — which is the
        // part a container platform's readiness probe gets wrong.
        using var _ = new AssertionScope();
        (await _http.GetAsync($"{_keycloak.ManagementUrl}/health/ready")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await _http.GetAsync($"{_keycloak.BaseUrl}/health/ready")).StatusCode
            .Should().NotBe(HttpStatusCode.OK, "health never appears on the public port");
    }

    private Task<HttpResponseMessage> TokenAsync(string clientId, string secret) =>
        _http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        }));

    private static async Task<JsonElement> ClaimsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var payload = body.GetProperty("access_token").GetString()!.Split('.')[1];
        return JsonDocument.Parse(
            Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')
                .PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '='))).RootElement;
    }

    /// <summary>The <c>aud</c> claim is a string or an array of strings, per RFC 7519.</summary>
    private static IEnumerable<string> Audiences(JsonElement claims) =>
        claims.GetProperty("aud") is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Select(a => a.GetString()!)
            : [claims.GetProperty("aud").GetString()!];
}
