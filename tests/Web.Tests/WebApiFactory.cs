using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CvManager.Web.Tests;

/// <summary>
/// The real Web API host over a throwaway Postgres. Only the connection string is overridden — the
/// host boots exactly as it does in development, so these tests exercise the production startup
/// path: the real EF migrations, the real dev seed, the app-wide authorization fallback policy, and
/// the real <c>GlobalExceptionHandler</c>. Anything Postgres-specific (partial unique indexes,
/// cascade deletes, date/enum mapping) is therefore in scope here in a way EF InMemory can never be.
/// </summary>
public sealed class WebApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // pgvector, not plain postgres: the migrations create the `vector` extension for the RAG chunk
    // store, so a stock postgres image fails to migrate.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Force the host to build now, so migrations and the seed run inside InitializeAsync
        // rather than inside the first test that happens to touch the API.
        using var _ = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development explicitly: it is the environment whose startup path applies migrations and
        // the seed, and whose appsettings supply the dev signing key. Left to default, the host
        // would boot as Production and refuse to start on the placeholder key.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
    }

    /// <summary>
    /// A client carrying a valid session bearer token, minted from the host's own Auth:Jwt config so
    /// it passes the same JWT validation the running app enforces. Mirrors
    /// <c>Agents.Tests/AuthTestExtensions</c> — the two hosts share one session token by design.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var config = Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MintHs256(key, config["Auth:Jwt:Issuer"] ?? "cvmanager", config["Auth:Jwt:Audience"] ?? "cvmanager-app"));
        return client;
    }

    private static string MintHs256(string key, string issuer, string audience)
    {
        static string B64(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" })));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["iss"] = issuer,
            ["aud"] = audience,
            ["nbf"] = now,
            ["exp"] = now + 3600,
        })));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return $"{header}.{payload}.{B64(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")))}";
    }
}

/// <summary>
/// One container and one host for the whole assembly — starting Postgres per class costs more than
/// the isolation is worth. Tests keep themselves apart by owning the rows they create (unique
/// emails, ids returned from POST) and never asserting on collection totals.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WebApiCollection : ICollectionFixture<WebApiFactory>
{
    public const string Name = "web-api";
}
