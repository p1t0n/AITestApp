using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Agents.Tests;

internal static class AuthTestExtensions
{
    /// <summary>
    /// A client carrying a valid session bearer token, minted from the host's own Auth:Jwt config
    /// so it passes the same JWT validation the running app enforces (the agent endpoints require
    /// authorization).
    /// </summary>
    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory)
        => factory.CreateAuthenticatedClient(Guid.NewGuid());

    /// <summary>Same, for a caller-chosen user id — live smokes that persist user-FK rows (e.g.
    /// roster-scan jobs) seed a real Users row and mint its token, mirroring production.</summary>
    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory, Guid userId)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");
        var issuer = config["Auth:Jwt:Issuer"] ?? "cvmanager";
        var audience = config["Auth:Jwt:Audience"] ?? "cvmanager-app";

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MintHs256(key, issuer, audience, userId));
        return client;
    }

    private static string MintHs256(string key, string issuer, string audience, Guid userId)
    {
        static string B64(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" })));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["iss"] = issuer,
            ["aud"] = audience,
            ["nbf"] = now,
            ["exp"] = now + 3600,
        })));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = B64(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }
}
