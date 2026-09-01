using System.Net.Http.Headers;
using System.Security.Cryptography;
using ExpertToJob.Application.Auth;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Web.Tests;

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
    /// A client carrying a valid session bearer token for a Service Manager, minted from the host's
    /// own Auth:Jwt config so it passes the same JWT validation the running app enforces. Mirrors
    /// <c>Agents.Tests/AuthTestExtensions</c> — the two hosts share one session token by design.
    /// </summary>
    public HttpClient CreateAuthenticatedClient() => CreateClientFor(UserRole.ServiceManager).Client;

    /// <summary>A client whose session belongs to an Expert — the role a self-serve signup gets.</summary>
    public HttpClient CreateExpertClient() => CreateClientFor(UserRole.Expert).Client;

    /// <summary>
    /// A client plus the account it belongs to. The account is a real row: the session token names
    /// it and the host re-reads its <c>TokenVersion</c> on every request, so a token for a user that
    /// does not exist is refused — which is exactly the revocation mechanism and means tests can no
    /// longer wave a bare signature at the API.
    /// </summary>
    public (HttpClient Client, User Account) CreateClientFor(UserRole role)
    {
        var account = CreateAccount(role);
        return (ClientForAccount(account), account);
    }

    /// <summary>A client for an account that already exists — used after its token version moves.</summary>
    public HttpClient ClientForAccount(User account)
    {
        var config = Services.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MintHs256(
                key,
                config["Auth:Jwt:Issuer"] ?? "experttojob",
                config["Auth:Jwt:Audience"] ?? "experttojob-app",
                account));
        return client;
    }

    /// <summary>Inserts an account in the given role. Passkey-less: no ceremony is run here.</summary>
    public User CreateAccount(UserRole role, UserStatus status = UserStatus.Active)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            ControlWordHash = "test-not-a-real-hash",
            Role = role,
            Status = status,
            TokenVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>
    /// An Expert session that owns the given roster row (P1T-182) — the pairing every own-row test
    /// needs: an account, a row, and the link between them written the way the claim flow will.
    /// </summary>
    public (HttpClient Client, User Account) CreateExpertClientOwning(Guid expertId)
    {
        var account = CreateAccount(UserRole.Expert);
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expert = db.Experts.Single(e => e.Id == expertId);
            expert.OwnerUserId = account.Id;
            db.SaveChanges();
        }

        return (ClientForAccount(account), account);
    }

    /// <summary>Points a roster row at an account, without going through any service.</summary>
    public void SetOwner(Guid expertId, Guid? ownerUserId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Experts.Single(e => e.Id == expertId).OwnerUserId = ownerUserId;
        db.SaveChanges();
    }

    /// <summary>Who owns a roster row, straight from the column.</summary>
    public Guid? OwnerOf(Guid expertId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Experts.AsNoTracking().Single(e => e.Id == expertId).OwnerUserId;
    }

    /// <summary>Removes an account, as erasure will — the session it minted must stop working.</summary>
    public void DeleteAccount(Guid userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.RemoveRange(db.Users.Where(u => u.Id == userId));
        db.SaveChanges();
    }

    /// <summary>Bumps an account's token version — the revocation switch — and returns the new value.</summary>
    public int RevokeSessions(Guid userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Id == userId);
        user.TokenVersion++;
        db.SaveChanges();
        return user.TokenVersion;
    }

    private static string MintHs256(string key, string issuer, string audience, User account)
    {
        static string B64(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" })));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = account.Id.ToString(),
            [SessionClaims.Role] = account.Role.ToString(),
            [SessionClaims.TokenVersion] = account.TokenVersion,
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
