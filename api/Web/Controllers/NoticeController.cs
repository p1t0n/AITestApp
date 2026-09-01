using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Compliance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// The versioned transparency notice (P1T-183). The reads are anonymous, deliberately and
/// necessarily: acknowledging the notice is required to register, and requiring somebody to
/// acknowledge a text they must first have an account to read would be a transparency breach
/// dressed as an auth rule (Art. 13, Art. 5(1)(a)).
///
/// <para>Any published version is fetchable by version, not just the current one — a recorded
/// acknowledgment proves nothing if the words acknowledged cannot be recovered afterwards.</para>
/// </summary>
[ApiController]
[Route("api/notice")]
public class NoticeController(
    IAppDbContext db,
    IProcessingRecordService records,
    TimeProvider clock) : ControllerBase
{
    /// <summary>The notice as it stands today — what a registration form renders.</summary>
    [AllowAnonymous]
    [HttpGet]
    public ActionResult<TransparencyNoticeDto> Current() => Ok(TransparencyNotice.Current);

    /// <summary>One published version, by its version string. 404 for a version never published.</summary>
    [AllowAnonymous]
    [HttpGet("{version}")]
    public ActionResult<TransparencyNoticeDto> ByVersion(string version) =>
        TransparencyNotice.Find(version) is { } notice
            ? Ok(notice)
            : NotFound(new { error = $"No transparency notice version '{version}' has been published." });

    /// <summary>
    /// What the signed-in account acknowledged, and whether a newer notice is waiting. The session
    /// response says the same thing at sign-in; this exists so a reload does not lose the fact, and
    /// so the banner is answered by the server rather than by whatever the browser kept.
    /// </summary>
    [Authorize(Policy = AuthPolicies.AnyRole)]
    [HttpGet("status")]
    public async Task<ActionResult<NoticeStatusResponse>> Status(CancellationToken ct)
    {
        var userId = SessionRevocation.UserId(User);
        var account = userId is null
            ? null
            : await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Role, u.AcknowledgedNoticeVersion })
                .FirstOrDefaultAsync(ct);

        return account is null
            ? Unauthorized(new { error = "The account this session names no longer exists." })
            : Ok(new NoticeStatusResponse(
                account.AcknowledgedNoticeVersion,
                TransparencyNotice.CurrentVersion,
                TransparencyNotice.PendingFor(account.Role, account.AcknowledgedNoticeVersion)));
    }

    /// <summary>
    /// Acknowledges a new notice version for the signed-in account. Nothing is gated on this — a
    /// changed notice notifies, it does not withhold anybody's data pending a click (EDPB: no
    /// re-collection on a change of information). It records the acknowledgment on the account and,
    /// when that account owns a roster row, appends the acknowledgment to the row's history <em>on
    /// the basis it is already on</em> — reading an updated notice is not a change of lawful basis.
    /// </summary>
    [Authorize(Policy = AuthPolicies.AnyRole)]
    [HttpPost("acknowledge")]
    public async Task<ActionResult<NoticeStatusResponse>> Acknowledge(
        AcknowledgeNoticeRequest request, CancellationToken ct)
    {
        if (!TransparencyNotice.IsPublished(request.Version))
        {
            return BadRequest(new
            {
                error = $"No transparency notice version '{request.Version}' has been published.",
            });
        }

        var userId = SessionRevocation.UserId(User);
        var user = userId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Unauthorized(new { error = "The account this session names no longer exists." });
        }

        user.AcknowledgedNoticeVersion = request.Version;
        user.NoticeAcknowledgedAt = clock.GetUtcNow();
        user.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var ownedExpertId = await db.Experts
            .AsNoTracking()
            .Where(e => e.OwnerUserId == user.Id)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        if (ownedExpertId is { } expertId)
        {
            await records.AcknowledgeNoticeAsync(expertId, request.Version, ct);
        }

        return Ok(new NoticeStatusResponse(
            user.AcknowledgedNoticeVersion,
            TransparencyNotice.CurrentVersion,
            TransparencyNotice.PendingFor(user.Role, user.AcknowledgedNoticeVersion)));
    }
}

/// <summary>What the signed-in account acknowledged, and whether a newer notice is waiting.</summary>
/// <param name="AcknowledgedVersion">The version on the account, or null if it never acknowledged one.</param>
/// <param name="CurrentVersion">The version published today.</param>
/// <param name="PendingVersion">
/// The current version when it differs from the acknowledged one, else null. Non-null means "show
/// them the new notice" — never "stop them doing anything".
/// </param>
public sealed record NoticeStatusResponse(
    string? AcknowledgedVersion, string CurrentVersion, string? PendingVersion);

/// <summary>The version the person says they have read.</summary>
public sealed record AcknowledgeNoticeRequest(string Version);
