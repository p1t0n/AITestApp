using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// Contesting an automated score (P1T-189) — the Art. 22(3) safeguards, which are obligations here
/// rather than good practice because we rely on Art. 22(2)(a) and concede the automation.
///
/// <para>Shaped like the claim surface it shares a page with: the class carries the wider audience
/// and each staff action narrows it. The one Expert-reachable action is contesting, which is
/// ownership-scoped underneath — somebody else's score is a 404, not a 403.</para>
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/contests")]
public class ContestsController(IContestService contests) : ControllerBase
{
    /// <summary>
    /// Asks for a human to look at one score, and says why. The "why" is Art. 22(3)'s right to
    /// express a view — a distinct right from contesting, and worth nothing unless it reaches the
    /// person who reviews.
    /// </summary>
    [Authorize(Policy = AuthPolicies.AnyRole)]
    [HttpPost]
    public Task<ContestQueueItemDto> Contest(ContestScoreRequest request, CancellationToken ct) =>
        contests.ContestAsync(request.ScoringCandidateId, request.View, ct);

    /// <summary>Everything waiting for a human. Lives beside the claim queue on the Users page.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet]
    public Task<IReadOnlyList<ContestQueueItemDto>> Open(CancellationToken ct) =>
        contests.OpenAsync(ct);

    /// <summary>
    /// Records that a human looked, and what they said back. That record <em>is</em> the safeguard:
    /// Art. 22(3) asks for human intervention, and an intervention nobody can evidence did not
    /// happen as far as an audit is concerned.
    /// </summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("{id:guid}/review")]
    public Task<ContestReviewDto> Review(Guid id, ReviewContestRequest request, CancellationToken ct) =>
        contests.ReviewAsync(id, request.Outcome, request.Response, ActingUserId(), ct);

    private Guid ActingUserId() =>
        SessionRevocation.UserId(User)
        ?? throw new ConflictException("This session does not name an account.");
}

/// <param name="View">The person's own words about the score. Optional — asking for a human to look
/// is a right on its own, and requiring an essay first would be a toll on it.</param>
public sealed record ContestScoreRequest(Guid ScoringCandidateId, string? View);

/// <param name="Outcome">One of <c>upheld</c> or <c>overturned</c>.</param>
/// <param name="Response">What the reviewer says back, in their own words.</param>
public sealed record ReviewContestRequest(string Outcome, string? Response);
