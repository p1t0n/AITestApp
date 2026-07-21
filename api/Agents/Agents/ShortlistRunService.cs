namespace EmployeeManager.Agents.Agents;

/// <summary>
/// What one shortlist run produced, shaped for any caller (the HTTP endpoint today, the staffing
/// pipeline next). Exactly one of <see cref="Response"/> and <see cref="FaultDetail"/> is non-null:
/// the composed response on success, or the upstream-fault detail when the model skipped the tool
/// or the tool reported a soft retrieval error. <see cref="Reply"/> is always present — tokens
/// were spent either way, so the caller meters it (under <see cref="AgentName"/>) before deciding
/// what to do with a fault.
/// </summary>
public sealed record ShortlistRunOutcome(
    string AgentName,
    AgentReply Reply,
    ShortlistResponse? Response,
    string? FaultDetail);

/// <summary>
/// The core of a shortlist run, extracted from the POST /agents/shortlist endpoint: run the
/// <see cref="ShortlistAgent"/>, then compose the full response via <see cref="ShortlistComposer"/>
/// — including the templated-rationale degrade and the corruption guard that keeps model text out
/// of the deterministic fields. No HTTP types, no cap-check, no metering: those stay with the
/// caller, which orchestrates them differently per surface.
///
/// Upstream-fault mapping to HTTP lives in the endpoint shell, not here. This service reports the
/// degraded-run fault as data (<see cref="ShortlistRunOutcome.FaultDetail"/>, because the reply
/// still has to be metered first) and lets <see cref="HttpRequestException"/> from the model/MCP
/// stack propagate; the shell turns both into a 502.
/// </summary>
public sealed class ShortlistRunService
{
    private readonly ShortlistAgent _agent;

    public ShortlistRunService(ShortlistAgent agent) => _agent = agent;

    /// <summary>Runs the agent for one typed request and composes the outcome.</summary>
    public async Task<ShortlistRunOutcome> RunAsync(ShortlistAgentRequest request, CancellationToken ct = default)
    {
        var outcome = await _agent.ShortlistAsync(request, ct);

        // No captured tool result (the model skipped the tool) or a soft retrieval error from the
        // tool (e.g. embedding backend down): upstream fault, surfaced as data so the caller can
        // meter the reply before mapping it.
        if (outcome.Tool is null || outcome.Tool.Error is not null)
        {
            return new ShortlistRunOutcome(
                _agent.Name,
                outcome.Reply,
                Response: null,
                outcome.Tool?.Error ?? "The agent did not produce a roster_shortlist_search result.");
        }

        return new ShortlistRunOutcome(
            _agent.Name, outcome.Reply, ShortlistComposer.Compose(outcome), FaultDetail: null);
    }
}
