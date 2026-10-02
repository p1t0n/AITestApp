using System.Data.Common;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
/// path: the real schema (applied first, as `api/Migrator` does), the app-wide fallback policy, and
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

        // The schema is `api/Migrator`'s now, not the host's (P1T-215), so the fixture applies it
        // the way a real run does: migrator first, API second. Doing it here also keeps the cost
        // inside InitializeAsync rather than inside the first test that happens to touch the API.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await DbInitializer.SeedAsync(db);
        }

        using var _ = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// The SQL the host's own queries send, so a test can assert that work the design says happens
    /// in the database really does (EXP-45). "Paged in SQL" and "paged over a materialised list"
    /// return exactly the same rows for any set a test can afford to seed — the difference only
    /// shows at a scale nobody runs in a test, and it is the whole point of the contract. Reading
    /// the command text is the only way to tell them apart cheaply.
    /// </summary>
    public SqlLog Sql { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development explicitly: it is the environment whose appsettings supply the dev signing
        // key. Left to default, the host would boot as Production and refuse to start on the
        // placeholder key.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // EF resolves IInterceptor registrations out of the application container, so this attaches
        // to the host's real DbContext without the fixture re-configuring it.
        builder.ConfigureServices(services => services.AddSingleton<IInterceptor>(Sql));
    }

    /// <summary>
    /// A client carrying a valid session bearer token for an Administrator, minted from the host's
    /// own Auth:Jwt config so it passes the same JWT validation the running app enforces. Mirrors
    /// <c>Agents.Tests/AuthTestExtensions</c> — the two hosts share one session token by design.
    /// </summary>
    public HttpClient CreateAuthenticatedClient() => CreateClientFor(UserRole.Administrator).Client;

    /// <summary>A client whose session belongs to a User — the role a self-serve signup gets.</summary>
    public HttpClient CreateUserClient() => CreateClientFor(UserRole.User).Client;

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
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", SessionTokenFor(Services, account));
        return client;
    }

    /// <summary>
    /// A session bearer for an account, minted from <em>a given host's</em> own Auth:Jwt config
    /// rather than this fixture's. Taking the provider as an argument is what lets a test reach a
    /// second host built with <c>WithWebHostBuilder</c> — a Production one signs with a different
    /// key, so a token minted here would not pass over there (EXP-74).
    /// </summary>
    public static string SessionTokenFor(IServiceProvider host, User account)
    {
        var config = host.GetRequiredService<IConfiguration>();
        var key = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey missing from test host config.");

        return MintHs256(
            key,
            SessionIdentity.Issuer,
            SessionIdentity.Audience,
            account);
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
    /// A User session that owns the given roster row (P1T-182) — the pairing every own-row test
    /// needs: an account, a row, and the link between them written the way the claim flow will.
    /// </summary>
    public (HttpClient Client, User Account) CreateUserClientOwning(Guid expertId)
    {
        var account = CreateAccount(UserRole.User);
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
/// A rolling record of the SQL the host sent. Safe to keep as one instance across the assembly
/// because <see cref="WebApiCollection"/> runs its classes one at a time — a test clears it, makes
/// its call, and reads what that call produced.
/// </summary>
public sealed class SqlLog : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    /// <summary>Everything sent since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<string> Commands
    {
        get { lock (_commands) { return _commands.ToList(); } }
    }

    public void Clear()
    {
        lock (_commands) { _commands.Clear(); }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command.CommandText);
        return new(result);
    }

    private void Record(string sql)
    {
        lock (_commands) { _commands.Add(sql); }
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
