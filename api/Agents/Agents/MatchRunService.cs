namespace EmployeeManager.Agents.Agents;

/// <summary>What one match run produced: the answer text, plus the reply carrying the token usage
/// the caller meters under <see cref="AgentName"/>.</summary>
public sealed record MatchRunOutcome(string AgentName, string Answer, AgentReply Reply);

/// <summary>The match step seam: lets the staffing pipeline consume the run service while its
/// tests substitute a fake (the real service needs a live agent stack).</summary>
public interface IMatchRunService
{
    /// <summary>Runs the agent for one employee/job-description pair.</summary>
    Task<MatchRunOutcome> RunAsync(Guid employeeId, string jobDescription, CancellationToken ct = default);
}

/// <summary>
/// The core of a match run, extracted from the POST /agents/match endpoint: build the prompt from
/// the typed fields (the template lives here, as the single source of truth) and run the match
/// agent. No HTTP types, no cap-check, no metering: those stay with the caller, which orchestrates
/// them differently per surface.
///
/// Upstream-fault mapping to HTTP lives in the endpoint shell, not here. This service lets
/// <see cref="HttpRequestException"/> from the model/MCP stack propagate; the shell turns it into
/// a 502.
/// </summary>
public sealed class MatchRunService : IMatchRunService
{
    private readonly IChatAgent _agent;

    public MatchRunService(IChatAgent agent) => _agent = agent;

    /// <summary>Runs the agent for one employee/job-description pair.</summary>
    public async Task<MatchRunOutcome> RunAsync(Guid employeeId, string jobDescription, CancellationToken ct = default)
    {
        var prompt = $"Assess employee {employeeId} against this job description:\n\n{jobDescription}";
        var reply = await _agent.AskAsync(prompt, ct);
        return new MatchRunOutcome(_agent.Name, reply.Text, reply);
    }
}
