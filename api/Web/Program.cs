using System.Text.Json.Serialization;
using ExpertToJob.Application;
using ExpertToJob.Infrastructure;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Web.Auth;
using ExpertToJob.Web.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Production refuses to boot on placeholder secrets (P1T-87): an empty or dev-marked JWT signing
// key must never sign real sessions. Dev values live in appsettings.Development.json.
if (builder.Environment.IsProduction())
{
    var signingKey = builder.Configuration["Auth:Jwt:SigningKey"];
    if (string.IsNullOrWhiteSpace(signingKey) || signingKey.StartsWith("dev-only-insecure"))
    {
        throw new InvalidOperationException(
            "Auth:Jwt:SigningKey is empty or the dev placeholder. Provide a real key via " +
            "environment (Auth__Jwt__SigningKey) or a secrets store before running in Production.");
    }
}

// The shared spine (P1T-214): OTLP export — only when OTEL_EXPORTER_OTLP_ENDPOINT is set — plus
// AspNetCore/HttpClient/Runtime instrumentation, HTTP resilience and the health checks. This host
// emitted no telemetry at all before; it was invisible in the dashboard while the other two were
// not.
builder.AddServiceDefaults();

// What this host adds on top of the generic instrumentation: Npgsql, whose tracing is native, so
// the passkey ceremonies and the retention sweep show the queries they run. Nothing else — an
// ExpertToJob.Web activity source is new instrumentation, not this refactor. Frozen in
// tests/ServiceDefaults.Tests/HostTelemetryFreezeTests.cs.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("experttojob-web"))
    .WithTracing(t => t.AddSource("Npgsql"))
    .WithMetrics(m => m.AddMeter("Npgsql"));

const string SpaCors = "spa";

builder.Services.AddControllers()
.AddJsonOptions(o =>
{
    // Serialize enums as their string names (matches the DB representation).
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApplication();

// Registered before AddInfrastructure so the DbContext picks it up: an Expert doing something with
// their own record resets their retention clock, and nobody else's write does (P1T-188).
builder.Services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor,
    ExpertToJob.Web.Compliance.ExpertActivityInterceptor>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

// The retention sweep: off unless a deployment turns it on, because the safe default for a job
// that deletes people is "not running" (P1T-188).
var retention = builder.Configuration.GetSection("Retention").Get<ExpertToJob.Web.Compliance.RetentionOptions>()
    ?? new ExpertToJob.Web.Compliance.RetentionOptions();
builder.Services.AddSingleton(retention);
builder.Services.AddScoped<ExpertToJob.Application.Compliance.IRetentionSweep,
    ExpertToJob.Application.Compliance.RetentionSweep>();
builder.Services.AddScoped<ExpertToJob.Application.Compliance.IRetentionErasure,
    ExpertToJob.Application.Compliance.ErasureService>();
builder.Services.AddHostedService<ExpertToJob.Web.Compliance.RetentionWorker>();

// Passwordless auth: WebAuthn ceremonies + shared session JWT. The signup/signin/recovery
// endpoints (separate issues) drive the ceremonies via IFido2 + IChallengeStore + IJwtTokenIssuer.
builder.Services.AddPasskeyAuth(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options => options.AddPolicy(SpaCors, policy => policy
    .WithOrigins("http://localhost:5173", "https://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();

// This host no longer owns the schema (P1T-215). `api/Migrator` applies the migrations and seeds
// the catalog, and it runs before anything that needs a database — which is what lets the MCP
// server start against a fresh one without the API ever having run. The demo roster went with it:
// `tools/SeedDemoRoster` is the only path that loads it now.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The first Service Manager (P1T-181). Runs in every environment: signup only makes Experts, so
// without this a fresh database has no account that can reach the roster. No-op when unconfigured.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seedEmail = app.Configuration[$"{AuthOptions.Section}:SeedServiceManagerEmail"];
    var outcome = await ServiceManagerBootstrapper.EnsureAsync(db, seedEmail, TimeProvider.System);
    if (outcome != BootstrapOutcome.NotConfigured)
    {
        app.Logger.LogInformation("Service Manager bootstrap for {Email}: {Outcome}.", seedEmail, outcome);
    }
}

app.UseCors(SpaCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapControllers();

app.Run();

// Exposed so a future integration-test host (WebApplicationFactory) can reference the entry point.
public partial class Program { }
