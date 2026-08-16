using System.Security.Claims;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using CvManager.Agents.Agents;
using CvManager.Agents.Auth;
using CvManager.Agents.Configuration;
using CvManager.Agents.Mcp;
using CvManager.Agents.Staffing;
using CvManager.Agents.Usage;
using CvManager.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Production refuses to boot on placeholder secrets (P1T-87). Dev values live in
// appsettings.Development.json and pair with the committed dev Keycloak realm.
if (builder.Environment.IsProduction())
{
    var signingKey = builder.Configuration["Auth:Jwt:SigningKey"];
    if (string.IsNullOrWhiteSpace(signingKey) || signingKey.StartsWith("dev-only-insecure"))
    {
        throw new InvalidOperationException(
            "Auth:Jwt:SigningKey is empty or the dev placeholder. Provide a real key via " +
            "environment (Auth__Jwt__SigningKey) before running in Production.");
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
        && string.IsNullOrWhiteSpace(builder.Configuration["Gemini:ApiKey"]))
    {
        throw new InvalidOperationException(
            "No Gemini API key. Set GEMINI_API_KEY before running in Production.");
    }

    foreach (var agent in builder.Configuration.GetSection("McpAuth").GetChildren())
    {
        if (string.IsNullOrWhiteSpace(agent["ClientSecret"]))
        {
            throw new InvalidOperationException(
                $"McpAuth:{agent.Key}:ClientSecret is empty. Provide the agent's Keycloak client " +
                "secret via environment before running in Production.");
        }
    }
}

// Tracing spine (P1T-94, see manuals/maf-otel-telemetry.md): every layer already emits spans as
// opt-in decorators; this host subscription + OTLP export is what turns them on. Exporter target
// is the Aspire dashboard from docker-compose (OTLP gRPC on localhost:4317 by default, override
// via OTEL_EXPORTER_OTLP_ENDPOINT). The exporter buffers and drops when the dashboard is down —
// the app runs unchanged without it. Sensitive content capture stays OFF (no prompts in spans).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("cvmanager-agents"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource(
            "Experimental.Microsoft.Extensions.AI",   // chat + execute_tool spans
            "Experimental.Microsoft.Agents.AI",       // invoke_agent spans
            "Microsoft.Agents.AI.Workflows",          // workflow_invoke / executor.process
            "Experimental.ModelContextProtocol",      // MCP client RPCs (context propagates via _meta)
            "System.Net.Http",
            "Npgsql")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddMeter(
            "Experimental.Microsoft.Extensions.AI",   // gen_ai.client.token.usage / operation.duration
            "Experimental.ModelContextProtocol",
            "System.Net.Http",
            "Npgsql")
        .AddOtlpExporter());

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
builder.Services.AddSingleton<RosterQaThreadStore>();
builder.Services.AddHttpClient();

// Chat model: provider-agnostic IChatClient over Gemini (OpenAI-compatible, free tier with an API
// PAT). One shared default client, plus a keyed client for any agent that overrides its model via
// Gemini:Agents:<agent>. Swap the backend here in one place for Azure OpenAI / Anthropic / etc.
builder.Services.AddGeminiChatClient(builder.Configuration);

// MCP access: each agent gets its own keyed client-credentials identity + tool source, bound to
// its McpAuth:<agent> config section. Register a new agent's identity here before its agent below.
builder.Services.AddAgentMcpIdentity(builder.Configuration, "roster-qa");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "cv-tailoring");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "match");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "shortlist");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "interview-kit");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "bench-report");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "resume-ingestion");
builder.Services.AddAgentMcpIdentity(builder.Configuration, "roster-scan");

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
// Since P1T-117 the shortlist splits into a deterministic retrieval (the MCP tool invoked with
// the extractor's requirements — same agent identity/scopes) and a tool-less rationale model call.
builder.Services.AddSingleton(sp => new ShortlistAgent(
    sp.ResolveAgentChatClient("shortlist")));
builder.Services.AddSingleton<IShortlistSearch>(sp => new McpShortlistSearch(
    sp.GetRequiredKeyedService<IMcpToolSource>("shortlist")));
builder.Services.AddSingleton(sp => new InterviewKitAgent(
    sp.ResolveAgentChatClient("interview-kit"),
    sp.GetRequiredKeyedService<IMcpToolSource>("interview-kit"),
    sp.GetRequiredService<ILoggerFactory>()));
// Bench report (P1T-104): server-composed aggregates (direct MCP employee_list + the proposals
// ledger), model writes narrative only. Scoped — it reads the DB through IAppDbContext.
builder.Services.AddScoped(sp => new BenchReportService(
    sp.GetRequiredKeyedService<IMcpToolSource>("bench-report"),
    sp.ResolveAgentChatClient("bench-report"),
    sp.GetRequiredService<CvManager.Application.Abstractions.IAppDbContext>(),
    sp.GetRequiredService<ILogger<BenchReportService>>()));

// The first mcp:write agent (P1T-92): stages resumes as draft employees; humans promote.
builder.Services.AddSingleton(sp => new ResumeIngestionAgent(
    sp.ResolveAgentChatClient("resume-ingestion"),
    sp.GetRequiredKeyedService<IMcpToolSource>("resume-ingestion"),
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ResumeIngestionRunService>();

// Run services: the endpoint cores (prompt/response composition around one agent run), shared
// with the staffing pipeline. The endpoints keep validation, cap-checks, metering, and HTTP
// fault mapping around them.
builder.Services.AddSingleton<ShortlistRunService>();
builder.Services.AddSingleton(sp => new MatchRunService(
    sp.GetServices<IChatAgent>().First(a => a.Name == "match")));
builder.Services.AddSingleton<IShortlistRunService>(sp => sp.GetRequiredService<ShortlistRunService>());
builder.Services.AddSingleton<IMatchRunService>(sp => sp.GetRequiredService<MatchRunService>());
// JD requirement extraction (P1T-116): tool-less structured call, the single source of
// requirements for Shortlist/Match/Interview Kit/Roster Scan (consumers wired in P1T-117).
builder.Services.AddSingleton<IJdRequirementExtractor>(sp => new JdRequirementExtractor(
    sp.ResolveAgentChatClient(JdRequirementExtractor.AgentName)));

// Roster Scan scoring transport (P1T-123): the sync-vs-batch seam. The limiter is process-wide —
// it protects the model's RPM across every concurrent scan, like the staffing throttle. The
// runner (P1T-124) consumes both.
builder.Services.AddSingleton(sp =>
    builder.Configuration.GetSection(CvManager.Agents.RosterScan.RosterScanOptions.Section)
        .Get<CvManager.Agents.RosterScan.RosterScanOptions>() ?? new CvManager.Agents.RosterScan.RosterScanOptions());
builder.Services.AddSingleton<System.Threading.RateLimiting.RateLimiter>(sp =>
    new System.Threading.RateLimiting.FixedWindowRateLimiter(new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
    {
        Window = TimeSpan.FromMinutes(1),
        PermitLimit = sp.GetRequiredService<CvManager.Agents.RosterScan.RosterScanOptions>().RequestsPerMinute,
        QueueLimit = int.MaxValue,
        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    }));
builder.Services.AddSingleton<CvManager.Agents.RosterScan.IScoringTransport>(sp =>
    new CvManager.Agents.RosterScan.QueuedSyncScoringTransport(
        sp.ResolveAgentChatClient("roster-scan"),
        sp.GetRequiredService<System.Threading.RateLimiting.RateLimiter>(),
        sp.GetRequiredService<CvManager.Agents.RosterScan.RosterScanOptions>(),
        sp.GetRequiredService<TimeProvider>()));

// Roster Scan runner (P1T-124): jobs drain through an in-process channel; the worker also sweeps
// for due paused jobs and restart orphans. The store/runner are scoped (EF-backed) — the worker
// opens a scope per job pass.
builder.Services.AddSingleton<CvManager.Agents.RosterScan.IRosterDigestSource>(sp =>
    new CvManager.Agents.RosterScan.McpRosterDigestSource(
        sp.GetRequiredKeyedService<IMcpToolSource>("roster-scan")));
builder.Services.AddScoped<CvManager.Agents.RosterScan.ScoringJobStore>();
builder.Services.AddScoped<CvManager.Agents.RosterScan.RosterScanRunner>();
builder.Services.AddSingleton<CvManager.Agents.RosterScan.RosterScanQueue>();
builder.Services.AddSingleton<CvManager.Agents.RosterScan.IRosterScanQueue>(sp =>
    sp.GetRequiredService<CvManager.Agents.RosterScan.RosterScanQueue>());
builder.Services.AddHostedService<CvManager.Agents.RosterScan.RosterScanWorker>();
// JD-only match (P1T-103): shortlist retrieval + per-candidate match fan-out, no narrative.
builder.Services.AddSingleton(sp => new JdMatchRunService(
    sp.GetRequiredService<IShortlistRunService>(),
    sp.GetRequiredService<IMatchRunService>(),
    sp.GetRequiredService<StaffingThrottle>()));

// The staffing pipeline (P1T-75): a MAF workflow over the run services plus a tool-less narrative
// call on the default chat client. The match throttle is process-wide — it protects the model
// endpoint's rate limit across all concurrent staffing requests — while the pipeline itself is
// scoped because it meters/cap-checks through the request-scoped usage services.
builder.Services.AddOptions<StaffingOptions>().Bind(builder.Configuration.GetSection(StaffingOptions.Section));
builder.Services.AddSingleton(sp => new StaffingThrottle(
    sp.GetRequiredService<IOptions<StaffingOptions>>().Value.MaxConcurrentMatches));
builder.Services.AddSingleton(StaffingRetryPolicy.Default);
// The proposal ledger (P1T-100): staffing runs persist a pending proposal; humans decide it.
builder.Services.AddScoped<StaffingProposalStore>();
builder.Services.AddScoped(sp => new StaffingPipeline(
    sp.GetRequiredService<IShortlistRunService>(),
    sp.GetRequiredService<IMatchRunService>(),
    sp.ResolveAgentChatClient("staffing"),
    sp.GetRequiredService<IUsageService>(),
    sp.GetRequiredService<IUsageMeter>(),
    sp.GetRequiredService<StaffingThrottle>(),
    sp.GetRequiredService<StaffingRetryPolicy>(),
    sp.GetRequiredService<ILogger<StaffingPipeline>>()));

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

// POST /agents/roster-qa  { "question": "...", "threadId"?: "..." }  ->  { "answer": "...", "threadId": "..." }
// Threaded sessions (P1T-93): an omitted/unknown/expired threadId transparently starts a fresh
// thread — the client detects context loss by the returned id changing. History is bounded by
// the store (last 10 turns) and its tokens are metered like any other prompt tokens.
app.MapPost("/agents/roster-qa", async (
    RosterQaRequest request,
    IEnumerable<IChatAgent> agents,
    RosterQaThreadStore threads,
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

    var agent = (RosterQaAgent)agents.First(a => a.Name == "roster-qa");
    try
    {
        var thread = threads.Resolve(userId, request.ThreadId);
        var reply = await agent.AskAsync(request.Question, thread.History, ct);
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, agent.Name, reply, ct: ct);
        }

        threads.Append(userId, thread.ThreadId, request.Question, reply.Text);
        return Results.Ok(new RosterQaResponse(reply.Text, thread.ThreadId));
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
            await meter.RecordAsync(uid, agent.Name, outcome.Reply, ct: ct);
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

// POST /agents/interview-kit  { "employeeId": "guid", "jobDescription": "..." }
// -> { "answer": "<markdown kit>", "questions": [{ question, probes?, evidence? }] }
// The answer is turn 1's markdown verbatim; structured questions come from turn 2, and every
// evidence quote is validated against the captured cv_get result (unverifiable quotes drop from
// the question, the question survives). Structured-turn corruption degrades to questions: [].
app.MapPost("/agents/interview-kit", async (
    InterviewKitRequest request,
    InterviewKitAgent agent,
    IJdRequirementExtractor extractor,
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

    var prompt = $"Build an interview kit for employee {request.EmployeeId} against this job description:\n\n{request.JobDescription}";

    try
    {
        // One extraction per JD (P1T-117): gap-targeting sees the structured requirements.
        // An extraction fault degrades to a plain-JD kit, never fails the call.
        var extraction = await extractor.ExtractAsync(request.JobDescription, ct);
        if (userId is { } xuid)
        {
            await meter.RecordAsync(xuid, JdRequirementExtractor.AgentName, extraction.Reply, ct: ct);
        }

        if (extraction.Requirements is { } extracted)
        {
            prompt += $"\n\n{extracted.ToPromptBlock()}";
        }

        var outcome = await agent.GenerateAsync(prompt, ct);
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, agent.Name, outcome.Reply, ct: ct);
        }
        return Results.Ok(InterviewKitComposer.Compose(outcome, loggerFactory.CreateLogger(nameof(InterviewKitComposer))));
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

// POST /agents/match — two modes (P1T-103):
//   { "employeeId": "guid", "jobDescription": "..." }        -> { "answer": "..." } (unchanged)
//   { "jobDescription": "...", "topK"? }                     -> { "requirements": [...],
//     "results": [{ employeeId, name, title, retrievalScore, status, score, band, answer?, error? }] }
// JD-only mode retrieves the top candidates via shortlist search and fans the match run out per
// candidate (staffing throttle); one candidate's fault degrades that entry, never the call.
app.MapPost("/agents/match", async (
    MatchRequest request,
    MatchRunService runner,
    JdMatchRunService jdRunner,
    IJdRequirementExtractor extractor,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    if (request.EmployeeId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "employeeId must be a real id when present." });
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
        if (request.EmployeeId is { } employeeId)
        {
            // One extraction per JD (P1T-117): the structured requirements ride into the match
            // prompt. An extraction fault degrades to a plain-JD match, never fails the call.
            var extraction = await extractor.ExtractAsync(request.JobDescription, ct);
            if (userId is { } xuid)
            {
                await meter.RecordAsync(xuid, JdRequirementExtractor.AgentName, extraction.Reply, ct: ct);
            }

            var run = await runner.RunAsync(employeeId, request.JobDescription, extraction.Requirements, ct);
            if (userId is { } uid)
            {
                await meter.RecordAsync(uid, run.AgentName, run.Reply, ct: ct);
            }
            return Results.Ok(new MatchResponse(run.Answer));
        }

        var outcome = await jdRunner.RunAsync(request.JobDescription, request.TopK, ct);

        // Meter first: tokens were spent even when the run degrades to a 502 below.
        if (userId is { } jdUid)
        {
            foreach (var m in outcome.Metered)
            {
                await meter.RecordAsync(jdUid, m.AgentName, m.Reply, m.Step, ct);
            }
        }

        if (outcome.FaultDetail is { } fault)
        {
            return Results.Problem(
                title: "Upstream dependency failed (JD-match retrieval).",
                detail: fault,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new JdMatchResponse(outcome.Requirements, outcome.Results));
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

        // Meter first: tokens were spent even when the run degrades to a 502 below. The
        // extraction call (P1T-117) meters under its own agent name.
        if (userId is { } uid)
        {
            if (run.ExtractionReply is { } extractionReply)
            {
                await meter.RecordAsync(uid, JdRequirementExtractor.AgentName, extractionReply, ct: ct);
            }

            await meter.RecordAsync(uid, run.AgentName, run.Reply, ct: ct);
        }

        // The run service reports a degraded run (extraction, retrieval, or a soft tool error)
        // as data; mapping it to HTTP is this shell's job — same philosophy as the catch.
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

// POST /agents/resume-ingestion  { "resumeText": "..." }
// -> IngestionResponse: the staged draft's id + created counts + proposals + degradation notes.
// Deterministic fields come from captured MCP tool results (never model prose). Failure ladder:
// no draft created -> 422 with the abort reason; child failures degrade into notes on a 200.
app.MapPost("/agents/resume-ingestion", async (
    ResumeIngestionRequest request,
    ResumeIngestionRunService runner,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.ResumeText))
    {
        return Results.BadRequest(new { error = "resumeText is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    try
    {
        var run = await runner.RunAsync(request.ResumeText, ct);

        // Meter first: tokens were spent even when the run aborted below.
        if (userId is { } uid)
        {
            await meter.RecordAsync(uid, run.AgentName, run.Reply, ct: ct);
        }

        // Core abort (no draft exists): the resume did not yield a valid employee even after the
        // self-correction retries — the caller's input is the problem, not an upstream fault.
        if (run.Response is null)
        {
            return Results.Problem(
                title: "The resume could not be staged as a draft employee.",
                detail: run.AbortDetail,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(run.Response);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "Upstream dependency failed (MCP server, auth, or model).",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization();

// POST /agents/staffing  { "jobDescription": "...", availableOn?, skillIds?, location?, minYears?, matchTop? }
// -> text/event-stream (SSE only — demo with `curl -N`). The pre-checks (auth, blank JD, cap)
// answer as plain HTTP before the stream opens; after that the run streams as the pinned SSE
// contract (see StaffingSse): step/stepFailed per stage transition, then exactly one terminal
// event — report (partial results ship degraded:true) or error (failed shortlist / unexpected
// fault). Metering and the mid-run cap re-checks live inside the pipeline; client disconnect
// cancels the in-flight run through the request-aborted token.
app.MapPost("/agents/staffing", async (
    StaffingRequest request,
    StaffingPipeline pipeline,
    StaffingProposalStore proposals,
    ClaimsPrincipal user,
    IUsageService usage,
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json,
    IOptions<StaffingOptions> staffing,
    ILoggerFactory loggerFactory,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.JobDescription))
    {
        return Results.BadRequest(new { error = "jobDescription is required." });
    }

    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, http.RequestAborted) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    await StaffingSse.StreamAsync(
        http.Response,
        pipeline,
        new StaffingPipelineRequest(
            request.JobDescription,
            request.AvailableOn,
            request.SkillIds,
            request.Location,
            request.MinYears,
            request.MatchTop),
        userId,
        json.Value.SerializerOptions,
        TimeSpan.FromSeconds(staffing.Value.SseKeepAliveSeconds),
        loggerFactory.CreateLogger(nameof(StaffingSse)),
        http.RequestAborted,
        persistProposal: (report, ct) => proposals.CreateAsync(userId, request.JobDescription, report, ct));
    return Results.Empty;
}).RequireAuthorization();

// POST /agents/bench-report  {}  ->  { "answer": "<markdown>", "stats": {...}, "notes": [...] }
// Every number is server-composed (direct MCP employee_list + the proposals ledger); the model
// only writes prose over them. Input failures degrade to leaner stats + notes; a model failure
// ships the deterministic fallback summary — this endpoint never 500s for upstream faults.
app.MapPost("/agents/bench-report", async (
    BenchReportService service,
    ClaimsPrincipal user,
    IUsageMeter meter,
    IUsageService usage,
    CancellationToken ct) =>
{
    var userId = user.GetUserId();
    if (userId is { } pre && await usage.FindExceededAsync(pre, ct) is { } exceeded)
    {
        return CapReached(exceeded);
    }

    var outcome = await service.RunAsync(ct);
    if (userId is { } uid && outcome.Reply is { } reply)
    {
        await meter.RecordAsync(uid, BenchReportService.AgentName, reply, ct: ct);
    }

    return Results.Ok(outcome.Response);
}).RequireAuthorization();

// GET /agents/staffing/proposals?status=pending -> the approval inbox, newest first (P1T-100).
app.MapGet("/agents/staffing/proposals", async (
    string? status, StaffingProposalStore proposals, CancellationToken ct) =>
{
    var list = await proposals.ListAsync(status, ct);
    return Results.Ok(list.Select(ProposalResponse.From).ToList());
}).RequireAuthorization();

// POST /agents/staffing/proposals/{id}/decision  { "decision": "approved"|"rejected", "note"? }
// The human write path: only an identified user can decide, a proposal is decided exactly once
// (repeat -> 409), and the agent layer never calls this — humans hold write authority.
app.MapPost("/agents/staffing/proposals/{id:guid}/decision", async (
    Guid id,
    ProposalDecisionRequest request,
    StaffingProposalStore proposals,
    ClaimsPrincipal user,
    CancellationToken ct) =>
{
    if (user.GetUserId() is not { } decidedBy)
    {
        return Results.Forbid();
    }

    var approve = request.Decision?.Trim().ToLowerInvariant() switch
    {
        "approved" or "approve" => true,
        "rejected" or "reject" => false,
        _ => (bool?)null,
    };
    if (approve is null)
    {
        return Results.BadRequest(new { error = "decision must be 'approved' or 'rejected'." });
    }

    var (result, proposal) = await proposals.DecideAsync(id, decidedBy, approve.Value, request.Note, ct);
    return result switch
    {
        ProposalDecisionResult.NotFound => Results.NotFound(),
        ProposalDecisionResult.AlreadyDecided => Results.Problem(
            title: "The proposal is already decided.",
            detail: $"Current status: {proposal!.Status}.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Ok(ProposalResponse.From(proposal!)),
    };
}).RequireAuthorization();

app.Run();

internal sealed record RosterQaRequest(string Question, string? ThreadId = null);
internal sealed record ResumeIngestionRequest(string ResumeText);
internal sealed record RosterQaResponse(string Answer, string ThreadId);
internal sealed record CvTailoringRequest(Guid EmployeeId, string JobDescription);
internal sealed record InterviewKitRequest(Guid EmployeeId, string JobDescription);
internal sealed record MatchRequest(Guid? EmployeeId, string JobDescription, int? TopK = null);
internal sealed record MatchResponse(string Answer);
internal sealed record JdMatchResponse(
    IReadOnlyList<string> Requirements,
    IReadOnlyList<JdMatchCandidateResult> Results);
internal sealed record ShortlistRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null,
    int? TopK = null);
internal sealed record StaffingRequest(
    string JobDescription,
    DateOnly? AvailableOn = null,
    Guid[]? SkillIds = null,
    string? Location = null,
    decimal? MinYears = null,
    int? MatchTop = null);
internal sealed record ProposalDecisionRequest(string? Decision, string? Note = null);
internal sealed record ProposalCandidateResponse(
    Guid EmployeeId, string Name, string Title, int Rank, int? MatchScore, string? MatchBand, string Rationale);
internal sealed record ProposalResponse(
    Guid Id,
    string JobDescription,
    string Status,
    DateTimeOffset CreatedAt,
    Guid? RecommendedEmployeeId,
    bool ReportDegraded,
    IReadOnlyList<ProposalCandidateResponse> Candidates,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionNote)
{
    public static ProposalResponse From(CvManager.Domain.Entities.StaffingProposal p) => new(
        p.Id,
        p.JobDescription,
        p.Status,
        p.CreatedAt,
        p.RecommendedEmployeeId,
        p.ReportDegraded,
        p.Candidates.Select(c => new ProposalCandidateResponse(
            c.EmployeeId, c.Name, c.Title, c.Rank, c.MatchScore, c.MatchBand, c.Rationale)).ToList(),
        p.DecidedByUserId,
        p.DecidedAt,
        p.DecisionNote);
}

// Exposed so the integration/smoke tests (WebApplicationFactory) can reference the entry point.
public partial class Program { }
