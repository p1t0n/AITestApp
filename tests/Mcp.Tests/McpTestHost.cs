using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EmployeeManager.Mcp.Tests;

/// <summary>
/// Shared in-memory MCP host for integration tests. Swaps the database to EF InMemory and
/// replaces the JWT validation parameters with a local test signing key so tests can mint
/// their own access tokens (with chosen scopes) and exercise the real JwtBearer + scope
/// policies hermetically, without a running Keycloak.
/// </summary>
internal static class McpTestHost
{
    public const string Issuer = "https://test-issuer.local";
    public const string Resource = "https://localhost/mcp";
    public const string ReadScope = "mcp:read";
    public const string WriteScope = "mcp:write";
    public const string AdminScope = "mcp:admin";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("cv-manager-mcp-test-signing-key-which-is-long-enough"));

    private static void UseInMemoryDatabase(IServiceCollection services, string dbName)
    {
        var toRemove = services.Where(d =>
            d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
            d.ServiceType == typeof(DbContextOptions) ||
            d.ServiceType == typeof(AppDbContext) ||
            (d.ServiceType.IsGenericType &&
             d.ServiceType.GetGenericTypeDefinition().Name == "IDbContextOptionsConfiguration`1"))
            .ToList();
        foreach (var d in toRemove) services.Remove(d);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
    }

    public static WebApplicationFactory<Program> CreateFactory(string dbName) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The semantic-search reconciler needs pgvector + a real embedding backend; keep its
            // background worker off in these in-memory MCP tests.
            builder.UseSetting("SearchIndex:Enabled", "false");
            builder.ConfigureServices(services => UseInMemoryDatabase(services, dbName));

            // Override JWT validation to trust locally-minted test tokens.
            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Authority = null;
                    o.MetadataAddress = null!;
                    o.RequireHttpsMetadata = false;
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = Issuer,
                        ValidateAudience = true,
                        ValidAudience = Resource,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = SigningKey,
                        ValidateLifetime = true,
                    };
                });
            });
        });

    /// <summary>
    /// Factory for the Keycloak e2e test: swaps the DB to InMemory and points JwtBearer at a real
    /// authorization server (validates the signature against its JWKS), instead of the local test key.
    /// </summary>
    public static WebApplicationFactory<Program> CreateFactoryWithAuthority(string dbName, string authority, string audience) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The semantic-search reconciler needs pgvector + a real embedding backend; keep its
            // background worker off in these in-memory MCP tests.
            builder.UseSetting("SearchIndex:Enabled", "false");
            builder.ConfigureServices(services => UseInMemoryDatabase(services, dbName));

            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Authority = authority;
                    o.MetadataAddress = null!;
                    o.RequireHttpsMetadata = false;
                    o.Audience = audience;
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = authority,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                    };
                });
            });
        });

    /// <summary>Mints a signed test access token carrying the given scopes (space-delimited "scope" claim).</summary>
    public static string MintToken(params string[] scopes) =>
        MintTokenFor(Issuer, Resource, scopes);

    /// <summary>Mints a token with an explicit issuer/audience (for negative-path tests).</summary>
    public static string MintTokenFor(string issuer, string audience, params string[] scopes)
    {
        var claims = new List<Claim>();
        if (scopes.Length > 0) claims.Add(new Claim("scope", string.Join(' ', scopes)));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Connects an MCP client carrying the given bearer token (defaults to all scopes).</summary>
    public static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> factory, string? token = null)
    {
        token ??= MintToken(ReadScope, WriteScope, AdminScope);

        var httpClient = factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = httpClient.BaseAddress!,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "mcp-tests",
            },
            httpClient);

        return await McpClient.CreateAsync(transport);
    }

    public static Employee SeedEmployee(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = "ada@example.com",
        };
        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }

    public static string Text(CallToolResult result) =>
        (result.StructuredContent?.ToString() ?? "")
        + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public static string IdOf(CallToolResult result)
    {
        using var doc = JsonDocument.Parse(Text(result));
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
