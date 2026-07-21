using System.Security.Claims;
using EmployeeManager.Agents.Agents;
using EmployeeManager.Agents.Auth;
using EmployeeManager.Agents.Configuration;
using EmployeeManager.Agents.Mcp;
using EmployeeManager.Agents.Usage;
using EmployeeManager.Infrastructure;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<McpServerOptions>()
    .Bind(builder.Configuration.GetSection(McpServerOptions.Section));

// Validate the shared session JWT issued by the Web host (same signing key/issuer/audience).
builder.Services.AddSessionJwtAuthentication(builder.Configuration);

// DB access for token-usage metering (and, next, per-user cap enforcement). Employee data still
// flows only through MCP; this is the operational usage log.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<UsageOptions>().Bind(builder.Configuration.GetSection(UsageOptions.Section));
builder.Services.AddScoped<IUsageMeter, UsageMeter>();
builder.Services.AddScoped<IUsageService, UsageService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();

// Chat model: provider-agnostic IChatClient over GitHub Models (OpenAI-compatible, free with a
// PAT). One shared default client, plus a keyed client for any agent that overrides its model via
// GitHubModels:Agents:<agent>. Swap the backend here in one place for Azure OpenAI / Anthropic / etc.
builder.Services.AddGitHubModelsChatClient(builder.Configuration);

// MCP access: each agent gets its own keyed client-credentials identity + tool source, bound to
// its McpAuth:<agent> config section. Register a new agent's identity here before its agent below.
builder.Services.AddAgentMcpIdentity(builder.Configuration, "roster-qa");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "cv-tailoring");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "match");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "shortlist");

// Agents. Add future agents (Resume Ingestion, Staffing/Match) here. Each resolves its own keyed
// IMcpToolSource (own MCP identity) and its model-appropriate chat client (default or override).
builder.Services.AddSingleton<IChatAgent>(sp => new RosterQaAgent(
    sp.ResolveAgentChatClient("roster-qa"),
    sp.GetRequiredKeyedService<IMcpToolSource>("roster-qa"),
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<IChatAgent>(sp => new MatchAgent(
    sp.ResolveAgentChatClient("match"),
    sp.GetRequiredKeyedService<IMcpToolSource>("match"),
    sp.GetRequiredService<ILoggerFactory>()));

// The CV tailoring and shortlist agents return structured outcomes (captured tool results +
// minimal model JSON) rather than free text, so they are registered as their own types instead
// of through the IChatAgent seam.
builder.Services.AddSingleton(sp => new CvTailoringAgent(
    sp.ResolveAgentChatClient("cv-tailoring"),
    sp.GetRequiredKeyedService<IMcpToolSource>("cv-tailoring"),
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton(sp => new ShortlistAgent(
    sp.ResolveAgentChatClient("shortlist"),
    sp.GetRequiredKeyedService<IMcpToolSource>("shortlist"),
    sp.GetRequiredService<ILoggerFactory>()));

// Run services: the endpoint cores (prompt/response composition around one agent run), shared
// with the staffing pipeline. The endpoints keep validation, cap-checks, metering, and HTTP
// fault mapping around them.
builder.Services.AddSingleton<ShortlistRunService>();
builder.Services.AddSingleton(sp => new MatchRunService(
    sp.GetServices<IChatAgent>().First(a => a.Name == "match")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Structured 429 the SPA renders in the Usage tab: which window, how much, and when it resets.
static IResult CapReached(WindowUsage w) => Results.Json(
    new { error = $"Your {w.Window} token cap has been reached.", window = w.Window, used = w.Used, cap = w.Cap, resetAt = w.ResetAt },
    statusCode: StatusCodes.Status429TooManyRequests);

// GET /agents/usage -> the current user's usage across all windows + per-agent breakdown.
app.MapGet("/agents/usage", async (ClaimsPrincipal user, IUsageService usage, CancellationToken ct) =>
{
    if (user.GetUserId() is not { } userId)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await usage.GetSnapshotAsync(userId, ct));
}).RequireAuthorization();

// POST /agents/roster-qa  { "question": "..." }  ->  { "answer": "..." }
app.MapPost("/agents/roster-qa", async (
    RosterQaRequest request,
    IEnumerable<IChatAgent> agents,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    var agent = agents.First(a => a.Name == "roster-qa");
    try
    {
        var reply = await agent.AskAsync(request.Question, ct);
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, agent.Name, reply, ct);
        }
        return Results.Ok(new RosterQaResponse(reply.Text));
    }
    catch (HttpRequestException ex)
    {
        // MCP server unreachable, Keycloak token failure, or model endpoint error: upstream fault.
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

// POST /agents/cv-tailoring  { "employeeId": "guid", "jobDescription": "..." }
// -> { "answer": "<markdown as before>", "rewrites": [{ experienceId, achievementId, original, rewritten }] }
// The answer is unchanged for existing consumers; rewrite ids/originals are composed from the
// captured cv_get result — never model text — and each rewrite passes the fabrication guard.
app.MapPost("/agents/cv-tailoring", async (
    CvTailoringRequest request,
    CvTailoringAgent agent,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    if (request.EmployeeId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "employeeId is required." });
    }

    if (string.IsNullOrWhiteSpace(request.JobDescription))
    {
        return Results.BadRequest(new { error = "jobDescription is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    // Compose the two typed fields into the prompt that opens the agent's 2-turn session.
    var prompt = $"Tailor the CV of employee {request.EmployeeId} to this job description:\n\n{request.JobDescription}";

    try
    {
        var outcome = await agent.TailorAsync(prompt, ct);
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, agent.Name, outcome.Reply, ct);
        }
        return Results.Ok(TailoringComposer.Compose(outcome, loggerFactory.CreateLogger(nameof(TailoringComposer))));
    }
    catch (HttpRequestException ex)
    {
        // MCP server unreachable, Keycloak token failure, or model endpoint error: upstream fault.
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

// POST /agents/match  { "employeeId": "guid", "jobDescription": "..." }  ->  { "answer": "..." }
app.MapPost("/agents/match", async (
    MatchRequest request,
    MatchRunService runner,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    if (request.EmployeeId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "employeeId is required." });
    }

    if (string.IsNullOrWhiteSpace(request.JobDescription))
    {
        return Results.BadRequest(new { error = "jobDescription is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    try
    {
        var run = await runner.RunAsync(request.EmployeeId, request.JobDescription, ct);
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, run.AgentName, run.Reply, ct);
        }
        return Results.Ok(new MatchResponse(run.Answer));
    }
    catch (HttpRequestException ex)
    {
        // MCP server unreachable, Keycloak token failure, or model endpoint error: upstream fault.
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

// POST /agents/shortlist  { "jobDescription": "...", availableOn?, skillIds?, location?, minYears?, topK? }
// -> { "requirements": [...], "candidates": [...] } (see ShortlistResponse). Deterministic fields
// (ids, scores, coverage, evidence) are composed from the captured tool result — never model text.
app.MapPost("/agents/shortlist", async (
    ShortlistRequest request,
    ShortlistRunService runner,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.JobDescription))
    {
        return Results.BadRequest(new { error = "jobDescription is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    try
    {
        var run = await runner.RunAsync(
            new ShortlistAgentRequest(
                request.JobDescription,
                request.AvailableOn,
                request.SkillIds,
                request.Location,
                request.MinYears,
                request.TopK),
            ct);

        // Meter first: tokens were spent even when the run degrades to a 502 below.
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, run.AgentName, run.Reply, ct);
        }

        // The run service reports a degraded run (model skipped the tool, or a soft retrieval
        // error) as data; mapping it to HTTP is this shell's job — same philosophy as the catch.
        if (run.Response is null)
        {
            return Results.Problem(
                title: "Upstream dependency failed (shortlist retrieval).",
                detail: run.FaultDetail,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(run.Response);
    }
    catch (HttpRequestException ex)
    {
        // MCP server unreachable, Keycloak token failure, or model endpoint error: upstream fault.
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

app.Run();

internal sealed record RosterQaRequest(string Question);
internal sealed record RosterQaResponse(string Answer);
internal sealed record CvTailoringRequest(Guid EmployeeId, string JobDescription);
internal sealed record MatchRequest(Guid EmployeeId, string JobDescription);
internal sealed record MatchResponse(string Answer);
internal sealed record ShortlistRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null,
    int? TopK = null);

// Exposed so the integration/smoke tests (WebApplicationFactory) can reference the entry point.
public partial class Program { }
