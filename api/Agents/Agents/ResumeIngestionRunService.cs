using System.Text.Json;

namespace ExpertToJob.Agents.Agents;

/// <summary>Counts of successfully created children, composed from captured tool calls.</summary>
public sealed record IngestionCreated(int Languages, int Skills, int Qualifications, int Experiences);

/// <summary>
/// The composed ingestion response. Every deterministic field (employee id, counts, notes,
/// duplicate warning) comes from captured tool results; only <see cref="Proposals"/> originates
/// in model output — proposals are extraction content by nature, and the agent is instructed to
/// list exactly the skills it did not add.
/// </summary>
public sealed record IngestionResponse(
    Guid EmployeeId,
    IngestionCreated Created,
    IReadOnlyList<string> Proposals,
    IReadOnlyList<string> Notes,
    string? DuplicateWarning,
    bool Degraded);

/// <summary>What one ingestion run produced, shaped for the endpoint: the composed response on
/// success, or the abort detail when no draft was created (core abort). The reply is always
/// present — tokens were spent either way, so the caller meters before mapping.</summary>
public sealed record IngestionRunOutcome(
    string AgentName,
    AgentReply Reply,
    IngestionResponse? Response,
    string? AbortDetail);

/// <summary>The model's minimal closing JSON: proposals + the abort flag.</summary>
internal sealed record IngestionClosing(string[]? Proposals, bool Aborted, string? AbortReason);

/// <summary>
/// The core of an ingestion run: run the <see cref="ResumeIngestionAgent"/>, then compose the
/// response from the captured tool-call chain. The failure ladder (P1T-80): no successful
/// employee_create_draft → the whole run aborts (no draft exists); failed child calls degrade —
/// each one becomes a note, the run still returns the draft. A child that failed and then
/// succeeded on a model retry counts as success (the self-correction loop working as designed).
/// </summary>
public sealed class ResumeIngestionRunService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ResumeIngestionAgent _agent;

    public ResumeIngestionRunService(ResumeIngestionAgent agent) => _agent = agent;

    public async Task<IngestionRunOutcome> RunAsync(string resumeText, CancellationToken ct = default)
    {
        var outcome = await _agent.IngestAsync(resumeText, ct);

        if (outcome.EmployeeId is not { } employeeId)
        {
            var closing = ParseClosing(outcome.ClosingJson);
            var createError = outcome.ToolCalls
                .LastOrDefault(c => c.Tool == "employee_create_draft" && !c.Succeeded)?.Error;
            return new IngestionRunOutcome(
                _agent.Name,
                outcome.Reply,
                Response: null,
                AbortDetail: createError
                             ?? closing?.AbortReason
                             ?? "The agent did not create a draft employee.");
        }

        return new IngestionRunOutcome(
            _agent.Name, outcome.Reply, Compose(employeeId, outcome), AbortDetail: null);
    }

    private static IngestionResponse Compose(Guid employeeId, ResumeIngestionOutcome outcome)
    {
        // A tool that eventually succeeded is a success; only items the model gave up on (failed
        // with no later success covering them) degrade. We approximate "gave up" per tool name by
        // pairing failures beyond the number of successes — precise enough for counts and honest
        // notes without parsing tool arguments.
        int Succeeded(string tool) => outcome.ToolCalls.Count(c => c.Tool == tool && c.Succeeded);

        var created = new IngestionCreated(
            Succeeded("language_add"),
            Succeeded("employee_skill_add"),
            Succeeded("qualification_add"),
            Succeeded("experience_add"));

        // A note only where the model gave up: the tool's LAST call failed. Failures that a
        // corrected retry later covered are the self-correction loop doing its job, not
        // degradation. (Per-item pairing is impossible without parsing arguments; last-call state
        // per tool is the honest approximation.)
        var notes = outcome.ToolCalls
            .Where(c => c.Tool != "employee_create_draft")
            .GroupBy(c => c.Tool)
            .Where(g => !g.Last().Succeeded)
            .Select(g =>
            {
                var failures = g.Count(c => !c.Succeeded);
                return $"{g.Key} failed {failures} time(s); some items were skipped. Last error: {g.Last().Error}";
            })
            .ToList();

        var closing = ParseClosing(outcome.ClosingJson);

        return new IngestionResponse(
            employeeId,
            created,
            closing?.Proposals ?? [],
            notes,
            outcome.DuplicateWarning,
            Degraded: notes.Count > 0);
    }

    private static IngestionClosing? ParseClosing(string text)
    {
        // The closing message is instructed to be bare JSON, but models occasionally fence it.
        var trimmed = text.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IngestionClosing>(trimmed[start..(end + 1)], Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
