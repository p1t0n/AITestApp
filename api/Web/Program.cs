using System.Text.Json.Serialization;
using ExpertToJob.Application;
using ExpertToJob.Infrastructure;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Web.Auth;
using ExpertToJob.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Apply migrations + seed sample data on startup for a frictionless dev experience.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbInitializer.SeedAsync(db);

    // Optional demo roster seed (P1T-51): off by default. Flip Seed:DemoRoster to load the
    // committed 500-expert dataset; Seed:DemoRosterCount limits it to the first N experts.
    // Idempotent — experts whose email already exists are skipped.
    if (app.Configuration.GetValue("Seed:DemoRoster", false))
    {
        var demoCount = app.Configuration.GetValue<int?>("Seed:DemoRosterCount");
        var demoResult = await DemoRosterSeeder.SeedAsync(db, DemoRosterSeeder.LoadCommittedDataset(), demoCount);
        app.Logger.LogInformation(
            "Demo roster seed: {Seeded} experts seeded, {Skipped} already present.",
            demoResult.Seeded, demoResult.Skipped);
    }
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
app.MapControllers();

app.Run();

// Exposed so a future integration-test host (WebApplicationFactory) can reference the entry point.
public partial class Program { }
