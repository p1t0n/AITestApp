using ExpertToJob.Infrastructure;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Migrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The one process that owns the schema (P1T-215). It runs, it finishes, it exits — the Web API used
// to do this on a Development start, which meant the MCP server could only ever run against a
// database the API had already visited.
var builder = Host.CreateApplicationBuilder(args);

// The shared telemetry and health spine (P1T-214). Deliberately no MapDefaultEndpoints(): a process
// that exits has nothing to probe, and there is no WebApplication here to map onto.
builder.AddServiceDefaults();

// ConnectionStrings:Default, read the same way every other process reads it.
builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

return await DatabaseBootstrap.RunAsync(
    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
    host.Services.GetRequiredService<ILogger<Program>>());

// Named so the logger above has a category, and so the freeze in tests/Migrator.Tests has a file to
// read. Top-level statements compile into an internal Program; this makes the intent explicit.
public partial class Program;
