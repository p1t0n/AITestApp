using System.Text.Json.Serialization;
using CvManager.Application;
using CvManager.Infrastructure;
using CvManager.Infrastructure.Persistence;
using CvManager.Web.Auth;
using CvManager.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string SpaCors = "spa";

builder.Services.AddControllers()
.AddJsonOptions(o =>
{
    // Serialize enums as their string names (matches the DB representation).
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

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
    // committed 500-employee dataset; Seed:DemoRosterCount limits it to the first N employees.
    // Idempotent — employees whose email already exists are skipped.
    if (app.Configuration.GetValue("Seed:DemoRoster", false))
    {
        var demoCount = app.Configuration.GetValue<int?>("Seed:DemoRosterCount");
        var demoResult = await DemoRosterSeeder.SeedAsync(db, DemoRosterSeeder.LoadCommittedDataset(), demoCount);
        app.Logger.LogInformation(
            "Demo roster seed: {Seeded} employees seeded, {Skipped} already present.",
            demoResult.Seeded, demoResult.Skipped);
    }
}

app.UseCors(SpaCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so a future integration-test host (WebApplicationFactory) can reference the entry point.
public partial class Program { }
