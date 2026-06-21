using System.ClientModel;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Auth;
using EmployeeManager.Agents.Configuration;
using EmployeeManager.Agents.Mcp;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GitHubModelsOptions>()
    .Bind(builder.Configuration.GetSection(GitHubModelsOptions.Section));
builder.Services.AddOptions<McpClientAuthOptions>()
    .Bind(builder.Configuration.GetSection(McpClientAuthOptions.Section));
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

// MCP access: client-credentials token + Streamable-HTTP tool source, shared by all agents.
builder.Services.AddSingleton<IAccessTokenProvider, ClientCredentialsTokenProvider>();
builder.Services.AddSingleton<IMcpToolSource, McpToolSource>();

// Agents. Add future agents (CV Tailoring, Resume Ingestion, Staffing/Match) here.
builder.Services.AddSingleton<IChatAgent, RosterQaAgent>();

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
    var answer = await agent.AskAsync(request.Question, ct);
    return Results.Ok(new RosterQaResponse(answer));
});

app.Run();

internal sealed record RosterQaRequest(string Question);
internal sealed record RosterQaResponse(string Answer);

// Exposed so the integration/smoke tests (WebApplicationFactory) can reference the entry point.
public partial class Program { }
