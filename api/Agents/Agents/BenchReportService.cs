using System.Text.Json;
using System.Text.Json.Nodes;
using CvManager.Agents.Mcp;
using CvManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Agents;

/// <summary>What one bench-report run produced. <see cref="Reply"/> is null when the narrative
/// call never reached the model (fallback answer shipped) — nothing to meter then.</summary>
public sealed record BenchReportOutcome(BenchReportResponse Response, AgentReply? Reply);

/// <summary>
/// The bench &amp; capability-gap report (P1T-104). The server composes every deterministic input
/// itself — roster stats via a DIRECT MCP <c>employee_list</c> call (no model in the loop; MCP
/// stays the employee-data boundary) and demand stats from the agents-owned staffing proposals
/// ledger — then a tool-less chat call writes the narrative over those aggregates. The model
/// receives numbers, never produces them. Every input failure degrades to leaner stats + a note;
/// a model failure degrades to the deterministic fallback summary. The run never throws for
/// anything short of a programming error.
/// </summary>
public sealed class BenchReportService(
    IMcpToolSource toolSource,
    IChatClient chatClient,
    IAppDbContext db,
    ILogger<BenchReportService> logger)
{
    public const string AgentName = "bench-report";

    private const string EmployeeListTool = "employee_list";

    private const string Instructions =
        """
        You are the Bench Report assistant for a CV Manager. You are given a JSON object of
        deterministic roster and staffing-demand aggregates. Write a concise management-facing
        markdown report with these sections:

        1. Bench pressure — how much capacity is free vs booked, and what that means.
        2. Demand signal — what the staffing proposals history says was asked for and decided
           (skip this section when proposal stats are absent).
        3. Capability gaps & risks — where the roster looks thin for the demand visible in the
           data, single-person dependencies suggested by the distributions, hiring suggestions.

        Use ONLY the numbers and strings in the JSON — never invent counts, names, skills, or
        percentages. Where the data cannot answer something, say so instead of guessing. Keep it
        under 400 words.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<BenchReportOutcome> RunAsync(CancellationToken ct = default)
    {
        var notes = new List<string>();

        var employees = await FetchEmployeesAsync(notes, ct);
        var proposals = await FetchProposalsAsync(notes, ct);
        var stats = BenchStatsComposer.Compose(employees, proposals);

        var (answer, reply) = await WriteNarrativeAsync(stats, notes, ct);
        return new BenchReportOutcome(new BenchReportResponse(answer, stats, notes), reply);
    }

    /// <summary>Roster stats via a direct (agent-less) MCP tool call — the model is not involved
    /// in producing numbers, but employee data still flows only through MCP.</summary>
    private async Task<IReadOnlyList<BenchEmployee>?> FetchEmployeesAsync(List<string> notes, CancellationToken ct)
    {
        try
        {
            var tools = await toolSource.GetToolsAsync(ct);
            if (tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == EmployeeListTool) is not { } list)
            {
                notes.Add("Roster stats unavailable (employee_list tool not exposed to this agent).");
                return null;
            }

            var result = await list.InvokeAsync(new AIFunctionArguments(), ct);
            var employees = ExtractEmployees(JsonSerializer.SerializeToNode(result, Json), depth: 0);
            if (employees is null)
            {
                notes.Add("Roster stats unavailable (unrecognized employee_list result shape).");
            }

            return employees;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Bench report could not fetch roster stats; degrading.");
            notes.Add("Roster stats unavailable (MCP server or auth failure).");
            return null;
        }
    }

    private async Task<IReadOnlyList<Domain.Entities.StaffingProposal>?> FetchProposalsAsync(
        List<string> notes, CancellationToken ct)
    {
        try
        {
            return await db.StaffingProposals.AsNoTracking()
                .Include(p => p.Candidates)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Bench report could not read the proposals ledger; degrading.");
            notes.Add("Staffing-demand stats unavailable (proposals ledger unreadable).");
            return null;
        }
    }

    private async Task<(string Answer, AgentReply? Reply)> WriteNarrativeAsync(
        BenchStats stats, List<string> notes, CancellationToken ct)
    {
        try
        {
            using var metering = Usage.MeteringScope.Begin();
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, Instructions),
                    new ChatMessage(ChatRole.User, JsonSerializer.Serialize(stats, Json)),
                ],
                options: null,
                cancellationToken: ct);

            var (modelId, latencyMs) = metering.Snapshot();
            var reply = new AgentReply(
                response.Text,
                response.Usage?.InputTokenCount ?? 0,
                response.Usage?.OutputTokenCount ?? 0,
                response.Usage?.TotalTokenCount ?? 0,
                modelId,
                latencyMs);

            return string.IsNullOrWhiteSpace(response.Text)
                ? (BenchStatsComposer.FallbackAnswer(stats), reply)
                : (response.Text, reply);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Bench report narrative call failed; shipping the deterministic fallback.");
            notes.Add("Narrative unavailable (model call failed); this is the deterministic summary.");
            return (BenchStatsComposer.FallbackAnswer(stats), null);
        }
    }

    /// <summary>Array-aware sibling of <see cref="ToolResultPayload"/>: employee_list returns a
    /// JSON array, possibly wrapped in an MCP envelope or a text content block. Public — pure and
    /// unit-tested directly, same convention as <c>GeminiCompatHandler.NormalizeFinishReasons</c>.</summary>
    public static IReadOnlyList<BenchEmployee>? ExtractEmployees(JsonNode? node, int depth)
    {
        if (node is null || depth > 3)
        {
            return null;
        }

        if (node is JsonArray array)
        {
            try
            {
                return array.Deserialize<List<BenchEmployee>>(Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            try
            {
                return ExtractEmployees(JsonNode.Parse(text), depth + 1);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var key in new[] { "structuredContent", "result", "text" })
        {
            if (obj[key] is { } inner && ExtractEmployees(inner, depth + 1) is { } fromInner)
            {
                return fromInner;
            }
        }

        if (obj["content"] is JsonArray content)
        {
            foreach (var block in content)
            {
                if (block?["text"] is { } blockText && ExtractEmployees(blockText, depth + 1) is { } payload)
                {
                    return payload;
                }
            }
        }

        return null;
    }
}
