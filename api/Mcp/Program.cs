using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EmployeeManager.Application;
using EmployeeManager.Infrastructure;
using EmployeeManager.Mcp;
using EmployeeManager.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Tool results serialize like the Web API: enums as string names, DateOnly as ISO dates.
var toolSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    Converters = { new JsonStringEnumConverter() },
};

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<EmployeeTools>(toolSerializerOptions)
    .WithTools<LanguageTools>(toolSerializerOptions)
    .WithTools<AvailabilityTools>(toolSerializerOptions)
    .WithTools<EmployeeSkillTools>(toolSerializerOptions)
    .WithTools<QualificationTools>(toolSerializerOptions)
    .WithTools<ExperienceTools>(toolSerializerOptions)
    .WithTools<AchievementTools>(toolSerializerOptions)
    .WithTools<ExperienceSkillTools>(toolSerializerOptions)
    .WithTools<CatalogTools>(toolSerializerOptions)
    .WithTools<CvTools>(toolSerializerOptions);

var app = builder.Build();

// Static bearer gate before the MCP endpoint — see PRD (auth deferred to OAuth later).
app.UseMcpBearerAuth();
app.MapMcp();

app.Run();

// Exposed so the integration smoke test (WebApplicationFactory) can reference the entry point.
public partial class Program { }
