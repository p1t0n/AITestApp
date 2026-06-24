using System.ClientModel;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Configuration;
using EmployeeManager.Agents.Mcp;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GitHubModelsOptions>()
    .Bind(builder.Configuration.GetSection(GitHubModelsOptions.Section));
builder.Services.AddOptions<McpServerOptions>()
    .Bind(builder.Configuration.GetSection(McpServerOptions.Section));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();

// Chat model: provider-agnostic IChatClient. Default backend = GitHub Models (OpenAI-compatible,
// free with a PAT). Swappable here in one line for Azure OpenAI / OpenAI / Anthropic / Ollama.
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubModelsOptions>>().Value;
    var apiKey = Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } envToken
        ? envToken
        : cfg.ApiKey;

    var openAi = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) });

    return openAi.GetChatClient(cfg.Model).AsIChatClient();
});

// MCP access: each agent gets its own keyed client-credentials identity + tool source, bound to
// its McpAuth:<agent> config section. Register a new agent's identity here before its agent below.
builder.Services.AddAgentMcpIdentity(builder.Configuration, "roster-qa");

// Agents. Add future agents (CV Tailoring, Resume Ingestion, Staffing/Match) here, each resolving
// its own keyed IMcpToolSource so it authenticates to MCP as its own Keycloak client.
builder.Services.AddSingleton<IChatAgent>(sp => new RosterQaAgent(
    sp.GetRequiredService<IChatClient>(),
    sp.GetRequiredKeyedService<IMcpToolSource>("roster-qa"),
    sp.GetRequiredService<ILoggerFactory>()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// POST /agents/roster-qa  { "question": "..." }  ->  { "answer": "..." }
app.MapPost("/agents/roster-qa", async (
    RosterQaRequest request,
    IEnumerable<IChatAgent> agents,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question is required." });
    }

    var agent = agents.First(a => a.Name == "roster-qa");
    try
    {
        var answer = await agent.AskAsync(request.Question, ct);
        return Results.Ok(new RosterQaResponse(answer));
    }
    catch (HttpRequestException ex)
    {
        // MCP server unreachable, Keycloak token failure, or model endpoint error: upstream fault.
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

internal sealed record RosterQaRequest(string Question);
internal sealed record RosterQaResponse(string Answer);

// Exposed so the integration/smoke tests (WebApplicationFactory) can reference the entry point.
public partial class Program { }
