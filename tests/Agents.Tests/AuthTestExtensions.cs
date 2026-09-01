using System.Security.Cryptography;
using ExpertToJob.Application.Auth;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

internal static class AuthTestExtensions
{
    /// <summary>
    /// A client carrying a valid session bearer token, minted from the host's own Auth:Jwt config
    /// so it passes the same JWT validation the running app enforces (the agent endpoints require
    /// authorization). The account is seeded as a Service Manager: the agent surfaces are staff
    /// surfaces, and since P1T-181 the host re-reads the named account's token version per request,
    /// so a token for a user that does not exist is refused.
    /// </summary>
    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory)
        => factory.CreateAuthenticatedClient(Guid.NewGuid());

    /// <summary>Same, for a caller-chosen user id — live smokes that persist user-FK rows (e.g.
    /// roster-scan jobs) seed a real Users row and mint its token, mirroring production.</summary>
    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory, Guid userId)
        => factory.CreateClientForRole(userId, UserRole.ServiceManager);

    /// <summary>
    /// A client for a seeded account in the given role. Seeding is idempotent for a repeated id, so
    /// a smoke that already inserted its own Users row keeps that row (and its role).
    /// </summary>
    public static HttpClient CreateClientForRole(
        this WebApplicationFactory<Program> factory, Guid userId, UserRole role)
    {
        var account = factory.EnsureAccount(userId, role);
        return factory.ClientForAccount(account);
    }

    /// <summary>A client for an account that already exists — used after its token version moves.</summary>
    public static HttpClient ClientForAccount(this WebApplicationFactory<Program> factory, User account)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");
        var issuer = config["Auth:Jwt:Issuer"] ?? "experttojob";
        var audience = config["Auth:Jwt:Audience"] ?? "experttojob-app";

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", MintHs256(key, issuer, audience, account));
        return client;
    }

    /// <summary>
    /// The Users row the session names, created if it is not there. Every agent request now reads
    /// it (token-version revocation lives in the JWT event), so a token without an account behind
    /// it is a 401 — the same as production.
    /// </summary>
    public static User EnsureAccount(
        this WebApplicationFactory<Program> factory, Guid userId, UserRole role = UserRole.ServiceManager)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = db.Users.FirstOrDefault(u => u.Id == userId);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = userId,
            Email = $"{role.ToString().ToLowerInvariant()}-{userId:N}@example.com",
            ControlWordHash = "test-not-a-real-hash",
            Role = role,
            Status = UserStatus.Active,
            TokenVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>Bumps the account's token version — the revocation switch — and returns the row.</summary>
    public static User RevokeSessions(this WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Id == userId);
        user.TokenVersion++;
        db.SaveChanges();
        return user;
    }

    /// <summary>A client whose token carries a caller-chosen issuer and audience, for the
    /// lockstep check: the identity comes from the Web host's shipped config (or deliberately does
    /// not), while the signing key still comes from the Agents host under test.</summary>
    public static HttpClient CreateClientWithToken(
        this WebApplicationFactory<Program> factory, string issuer, string audience)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");

        var account = factory.EnsureAccount(Guid.NewGuid());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", MintHs256(key, issuer, audience, account));
        return client;
    }

    /// <summary>A token minted for an account but deliberately missing a claim, or carrying a stale
    /// one. Used to prove the host rejects what it cannot check.</summary>
    public static HttpClient CreateClientWithClaims(
        this WebApplicationFactory<Program> factory, Guid userId, string? role, int? tokenVersion)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");

        var claims = new Dictionary<string, object>();
        if (role is not null) claims[SessionClaims.Role] = role;
        if (tokenVersion is not null) claims[SessionClaims.TokenVersion] = tokenVersion.Value;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                MintHs256(
                    key,
                    config["Auth:Jwt:Issuer"] ?? "experttojob",
                    config["Auth:Jwt:Audience"] ?? "experttojob-app",
                    userId,
                    claims));
        return client;
    }

    private static string MintHs256(string key, string issuer, string audience, User account) =>
        MintHs256(key, issuer, audience, account.Id, new Dictionary<string, object>
        {
            [SessionClaims.Role] = account.Role.ToString(),
            [SessionClaims.TokenVersion] = account.TokenVersion,
        });

    private static string MintHs256(
        string key, string issuer, string audience, Guid userId, Dictionary<string, object> extra)
    {
        static string B64(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" })));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>(extra)
        {
            ["sub"] = userId.ToString(),
            ["iss"] = issuer,
            ["aud"] = audience,
            ["nbf"] = now,
            ["exp"] = now + 3600,
        };
        var payload = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = B64(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }
}
