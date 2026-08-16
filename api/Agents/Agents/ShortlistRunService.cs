namespace CvManager.Agents.Agents;

/// <summary>
/// What one shortlist run produced, shaped for any caller (the HTTP endpoint today, the staffing
/// pipeline next). Exactly one of <see cref="Response"/> and <see cref="FaultDetail"/> is non-null:
/// the composed response on success, or the upstream-fault detail when extraction, retrieval, or
/// the tool reported a soft error. <see cref="Reply"/> is the shortlist-attributed reply (the
/// rationale call; zero-token when the run faulted before it) and <see cref="ExtractionReply"/>
/// carries the JD-extraction tokens (metered under <c>jd-extraction</c>, null only when
/// extraction never ran) — tokens were spent either way, so the caller meters both before
/// deciding what to do with a fault.
/// </summary>
public sealed record ShortlistRunOutcome(
    string AgentName,
    AgentReply Reply,
    ShortlistResponse? Response,
    string? FaultDetail,
    AgentReply? ExtractionReply = null);

/// <summary>The shortlist step seam: lets the staffing pipeline consume the run service while its
/// tests substitute a fake (the real service needs a live agent stack).</summary>
public interface IShortlistRunService
{
    /// <summary>Runs extraction → retrieval → rationales for one typed request.</summary>
    Task<ShortlistRunOutcome> RunAsync(ShortlistAgentRequest request, CancellationToken ct = default);
}

/// <summary>
/// The core of a shortlist run (P1T-117): extract the JD's requirements once
/// (<see cref="IJdRequirementExtractor"/> — the single source for every consumer), invoke
/// <c>roster_shortlist_search</c> deterministically with the extracted texts
/// (<see cref="IShortlistSearch"/> — the model no longer picks tool arguments), then have the
/// rationale model annotate the captured evidence and compose via <see cref="ShortlistComposer"/>
/// (templated-rationale degrade and the corruption guard included). No HTTP types, no cap-check,
/// no metering: those stay with the caller. Faults are reported as data (the replies still have
/// to be metered first); transport exceptions propagate — the shell maps them to 502.
/// </summary>
public sealed class ShortlistRunService : IShortlistRunService
{
    /// <summary>The extractor prompt asks for 3-8 requirements; retrieval is capped to match.</summary>
    private const int MaxRequirements = 8;

    private static readonly AgentReply EmptyReply = new("", 0, 0, 0);

    private readonly IJdRequirementExtractor _extractor;
    private readonly IShortlistSearch _search;
    private readonly ShortlistAgent _agent;

    public ShortlistRunService(IJdRequirementExtractor extractor, IShortlistSearch search, ShortlistAgent agent)
    {
        _extractor = extractor;
        _search = search;
        _agent = agent;
    }

    /// <summary>Runs extraction → retrieval → rationales for one typed request.</summary>
    public async Task<ShortlistRunOutcome> RunAsync(ShortlistAgentRequest request, CancellationToken ct = default)
    {
        var extraction = await _extractor.ExtractAsync(request.JobDescription, ct);
        if (extraction.Requirements is null)
        {
            return new ShortlistRunOutcome(
                _agent.Name, EmptyReply, Response: null,
                extraction.FaultDetail ?? "JD requirement extraction failed.",
                extraction.Reply);
        }

        var requirements = extraction.Requirements.Requirements
            .Select(r => r.Text.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRequirements)
            .ToList();
        if (requirements.Count == 0)
        {
            return new ShortlistRunOutcome(
                _agent.Name, EmptyReply, Response: null,
                "JD requirement extraction produced no requirements.",
                extraction.Reply);
        }

        var payload = await _search.SearchAsync(requirements, request, ct);
        if (payload is null || payload.Error is not null)
        {
            return new ShortlistRunOutcome(
                _agent.Name, EmptyReply, Response: null,
                payload?.Error ?? "The roster_shortlist_search result was unreadable.",
                extraction.Reply);
        }

        var reply = await _agent.RationalesAsync(request.JobDescription, payload, ct);
        var response = ShortlistComposer.Compose(
            new ShortlistAgentOutcome(reply, requirements, payload), extraction.Requirements);
        return new ShortlistRunOutcome(_agent.Name, reply, response, FaultDetail: null, extraction.Reply);
    }
}
