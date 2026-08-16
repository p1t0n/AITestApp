using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using CvManager.Application;
using CvManager.Infrastructure;
using CvManager.Infrastructure.Embeddings;
using CvManager.Infrastructure.Search;
using CvManager.Mcp;
using CvManager.Mcp.Search;
using CvManager.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Tracing spine (P1T-94): the MCP SDK instruments itself and propagates trace context via
// JSON-RPC _meta, Npgsql tracing is native — this subscription + OTLP export turns both on.
// Target is the Aspire dashboard from docker-compose (OTLP gRPC localhost:4317 by default);
// the app runs unchanged when the dashboard is down.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("cvmanager-mcp"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource(
            "Experimental.ModelContextProtocol",
            "Experimental.Microsoft.Extensions.AI",   // embedding generator calls
            "System.Net.Http",
            "Npgsql")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddMeter(
            "Experimental.ModelContextProtocol",
            "Experimental.Microsoft.Extensions.AI",
            "System.Net.Http",
            "Npgsql")
        .AddOtlpExporter());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Embedding backend for semantic roster search (reconciliation worker + search query).
builder.Services.AddGeminiEmbeddings(builder.Configuration);
builder.Services.AddSearchIndexing(builder.Configuration);
builder.Services.AddHostedService<ReconcileWorker>();

// OAuth 2.1: this server is the Resource Server. Keycloak (the Authorization Server) issues
// tokens and runs the PKCE auth-code flow; here we only validate JWTs and advertise the AS.
var authority = builder.Configuration["Mcp:Authority"] ?? "http://localhost:8080/realms/cv-manager";
var resource = builder.Configuration["Mcp:Resource"] ?? "https://localhost/mcp";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = resource;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    })
    .AddMcp(options =>
    {
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = resource,
            AuthorizationServers = { authority },
            ScopesSupported = { McpScopes.Read, McpScopes.Write, McpScopes.Admin },
            BearerMethodsSupported = { "header" },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(McpScopes.Read, p => p.RequireAssertion(McpScopes.Has(McpScopes.Read)));
    options.AddPolicy(McpScopes.Write, p => p.RequireAssertion(McpScopes.Has(McpScopes.Write)));
    options.AddPolicy(McpScopes.Admin, p => p.RequireAssertion(McpScopes.Has(McpScopes.Admin)));
});

// Tool results serialize like the Web API: enums as string names, DateOnly as ISO dates.
var toolSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    Converters = { new JsonStringEnumConverter() },
};

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithTools<EmployeeTools>(toolSerializerOptions)
    .WithTools<LanguageTools>(toolSerializerOptions)
    .WithTools<AvailabilityTools>(toolSerializerOptions)
    .WithTools<EmployeeSkillTools>(toolSerializerOptions)
    .WithTools<QualificationTools>(toolSerializerOptions)
    .WithTools<ExperienceTools>(toolSerializerOptions)
    .WithTools<AchievementTools>(toolSerializerOptions)
    .WithTools<ExperienceSkillTools>(toolSerializerOptions)
    .WithTools<CatalogTools>(toolSerializerOptions)
    .WithTools<CvTools>(toolSerializerOptions)
    .WithTools<RosterSearchTools>(toolSerializerOptions)
    .WithTools<RosterShortlistTools>(toolSerializerOptions)
    .WithTools<RosterStyleTools>(toolSerializerOptions)
    .WithTools<RosterDigestTools>(toolSerializerOptions);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapMcp().RequireAuthorization();

app.Run();

// Exposed so the integration smoke test (WebApplicationFactory) can reference the entry point.
public partial class Program { }
