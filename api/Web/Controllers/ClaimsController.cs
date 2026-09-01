using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Claims;
using ExpertToJob.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// The claim surface (P1T-184): the Service Manager's queue, claim codes, and the one action an
/// Expert may take here — redeeming a code they were handed.
///
/// <para>Staff-only method by method apart from that one action, because everything else is a
/// decision about somebody else's row — the class carries the wider audience and each staff action
/// narrows it, the shape <c>ExpertsController</c> already uses, because both policies have to pass
/// and a staff-only class would refuse the Expert action outright.</para>
///
/// <para>The acting account is read from the session here and passed down, so the Application layer
/// never has to guess who decided.</para>
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/claims")]
public class ClaimsController(IClaimService claims) : ControllerBase
{
    /// <summary>Open claims and raised flags, oldest first.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet]
    public Task<IReadOnlyList<ClaimQueueItemDto>> Open(CancellationToken ct) => claims.OpenAsync(ct);

    /// <summary>Who owns one roster row. Read separately from the row itself so the agent-facing
    /// projection does not carry a staff-only field on every model call.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet("ownership/{expertId:guid}")]
    public Task<ExpertOwnershipDto> Ownership(Guid expertId, CancellationToken ct) =>
        claims.OwnershipAsync(expertId, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("{id:guid}/approve")]
    public Task<ClaimQueueItemDto> Approve(Guid id, CancellationToken ct) =>
        claims.ApproveAsync(id, ActingUserId(), ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("{id:guid}/reject")]
    public Task<ClaimQueueItemDto> Reject(Guid id, CancellationToken ct) =>
        claims.RejectAsync(id, ActingUserId(), ct);

    /// <summary>Issues a single-use code for a row. The plaintext in the response is the only copy
    /// that will ever exist — the database keeps a hash — so the screen must say so.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("codes")]
    public Task<ClaimCodeIssuedDto> IssueCode(IssueClaimCodeRequest request, CancellationToken ct) =>
        claims.IssueCodeAsync(request.ExpertId, ActingUserId(), ct);

    /// <summary>
    /// The one Expert-reachable action here. Redeeming binds ownership with no approval step,
    /// because a code a Service Manager handed over in person is the only proof this service has
    /// that is stronger than an unverified email match.
    /// </summary>
    [Authorize(Policy = AuthPolicies.AnyRole)]
    [HttpPost("redeem")]
    public async Task<ActionResult<RedeemClaimCodeResponse>> Redeem(
        RedeemClaimCodeRequest request, CancellationToken ct) =>
        Ok(new RedeemClaimCodeResponse(await claims.RedeemCodeAsync(request.Code, ActingUserId(), ct)));

    /// <summary>
    /// Unbinds a row. The consequence chains and the caller's UI has to say so: revoked means
    /// unowned, which means legitimate interest, which means the row is no longer scanned — so this
    /// button removes somebody from consideration.
    /// </summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RevokeOwnershipRequest request, CancellationToken ct)
    {
        await claims.RevokeAsync(request.ExpertId, ActingUserId(), ct);
        return NoContent();
    }

    /// <summary>
    /// The account behind this request. A session that carries no usable subject cannot decide
    /// anything — it reaches here only if authentication let it through without one, which is a bug
    /// rather than a request to handle gracefully.
    /// </summary>
    private Guid ActingUserId() =>
        SessionRevocation.UserId(User)
        ?? throw new ConflictException("This session does not name an account.");
}

public sealed record IssueClaimCodeRequest(Guid ExpertId);

public sealed record RedeemClaimCodeRequest(string Code);

/// <summary>The row the redeemer now owns — the SPA sends them to it.</summary>
public sealed record RedeemClaimCodeResponse(Guid ExpertId);

public sealed record RevokeOwnershipRequest(Guid ExpertId);
