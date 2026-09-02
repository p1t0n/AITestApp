using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Visibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// The pause control (P1T-185) — an Expert taking themselves off the bench, and putting themselves
/// back on it.
///
/// <para>Routed under <c>api/me</c> and carrying no expert id anywhere, because the row is always
/// the caller's own. Hiding is the Expert's act alone: a Service Manager who wants somebody off the
/// bench deactivates their account, and staff cannot un-hide somebody who hid themselves. That rule
/// is expressed here by the shape of the URL rather than by a check inside it — there is nothing to
/// forget, and nothing to talk into hiding a stranger.</para>
///
/// <para>Pause and delete are deliberately different kinds of thing and must stay two controls
/// (P1T-171): with no email there is no way to reach somebody who deleted when they meant to
/// pause.</para>
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/me/visibility")]
public class VisibilityController(IExpertVisibilityService visibility) : ControllerBase
{
    /// <summary>Whether the caller's own row is paused, and since when.</summary>
    [HttpGet]
    public Task<ExpertVisibilityDto> Mine(CancellationToken ct) =>
        visibility.MineAsync(ActingUserId(), ct);

    [HttpPost("hide")]
    public Task<ExpertVisibilityDto> Hide(CancellationToken ct) =>
        visibility.HideMineAsync(ActingUserId(), ct);

    [HttpPost("unhide")]
    public Task<ExpertVisibilityDto> Unhide(CancellationToken ct) =>
        visibility.UnhideMineAsync(ActingUserId(), ct);

    private Guid ActingUserId() =>
        SessionRevocation.UserId(User)
        ?? throw new ConflictException("This session does not name an account.");
}
