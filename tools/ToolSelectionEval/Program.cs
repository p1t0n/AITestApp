using System.ClientModel;
using System.Net.Http.Json;
using System.Text.Json;
using CvManager.ToolSelectionEval;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;

// Tool-selection eval CLI (P1T-127).
//
//   GEMINI_API_KEY=<key> dotnet run --project tools/ToolSelectionEval -- \
//     [--mcp http://localhost:5100] [--authority http://localhost:8080/realms/cv-manager] \
//     [--client agent-roster-qa] [--secret agent-roster-qa-secret] \
//     [--model gemini-3.5-flash-lite] [--output report.md] [--delay 4]
//
// Connects to the RUNNING MCP server (client-credentials against the dev Keycloak realm), lists
// the real tools — descriptions and all — and measures which one the real model picks first for
// each golden prompt. Exit code 0 = floors green, 1 = a floor violated, 2 = bad usage.

var mcpUrl = "http://localhost:5100";
var authority = "http://localhost:8080/realms/cv-manager";
var clientId = "agent-roster-qa";
var clientSecret = "agent-roster-qa-secret";
var model = "gemini-3.5-flash-lite";
string? outputPath = null;
var delaySeconds = 4.0;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--mcp" when i + 1 < args.Length: mcpUrl = args[++i]; break;
        case "--authority" when i + 1 < args.Length: authority = args[++i]; break;
        case "--client" when i + 1 < args.Length: clientId = args[++i]; break;
        case "--secret" when i + 1 < args.Length: clientSecret = args[++i]; break;
        case "--model" when i + 1 < args.Length: model = args[++i]; break;
        case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
        case "--delay" when i + 1 < args.Length && double.TryParse(args[i + 1], out var d): delaySeconds = d; i++; break;
        default:
            Console.Error.WriteLine("Usage: dotnet run -- [--mcp url] [--authority url] [--client id] [--secret s] [--model id] [--output path.md] [--delay seconds]");
            return 2;
    }
}

var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("GEMINI_API_KEY is not set.");
    return 1;
}

// Client-credentials token for the MCP listing (read scope suffices — nothing is invoked).
using var http = new HttpClient();
var tokenResponse = await http.PostAsync($"{authority}/protocol/openid-connect/token",
    new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
        ["scope"] = "mcp:read",
    }));
tokenResponse.EnsureSuccessStatusCode();
var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>())
    .GetProperty("access_token").GetString()!;

await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(mcpUrl),
    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
}));
var tools = await mcp.ListToolsAsync();
Console.Error.WriteLine($"Listed {tools.Count} tools from {mcpUrl}.");

var chat = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
{
    Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
}).GetChatClient(model).AsIChatClient();

var prompts = GoldenPromptSet.Load();
Console.Error.WriteLine($"Running {prompts.Count} golden prompts on {model} (delay {delaySeconds}s)...");
var aggregate = await ToolSelectionRunner.RunAsync(
    chat, tools.Cast<AIFunction>().ToList(), prompts,
    TimeSpan.FromSeconds(delaySeconds), Console.Error.WriteLine);

var report = ToolSelectionReport.Render(aggregate, model, DateOnly.FromDateTime(DateTime.UtcNow));
if (outputPath is not null)
{
    await File.WriteAllTextAsync(outputPath, report);
    Console.Error.WriteLine($"Report written to {outputPath}");
}
else
{
    Console.WriteLine(report);
}

var violations = ToolSelectionReport.GateViolations(aggregate);
if (violations.Count > 0)
{
    Console.Error.WriteLine("GATE VIOLATIONS:");
    foreach (var v in violations)
    {
        Console.Error.WriteLine($"  - {v}");
    }

    return 1;
}

Console.Error.WriteLine("All gates green.");
return 0;
