using System.Text.Json.Serialization;
using EmployeeManager.Application;
using EmployeeManager.Infrastructure;
using EmployeeManager.Infrastructure.Persistence;
using EmployeeManager.Web.Infrastructure;
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
}

app.UseCors(SpaCors);
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so a future integration-test host (WebApplicationFactory) can reference the entry point.
public partial class Program { }
