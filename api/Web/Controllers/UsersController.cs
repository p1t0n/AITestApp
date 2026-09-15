using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// User management. Service Manager only, wholesale (P1T-181) — the token-cap and status fields
/// here are staffing data, so there is no filtered Expert view of this controller; an Expert gets a
/// separate narrow my-account surface instead. Not exposed over MCP.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Administrator)]
[Route("api/users")]
public class UsersController(IUserService users, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserSummaryDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<UserDetailDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<UserDetailDto> Update(Guid id, UpdateUserDto dto, CancellationToken ct) =>
        users.UpdateAsync(id, dto, ct);

    /// <summary>
    /// Moves an account between roles (P1T-238). Its own endpoint rather than a field on
    /// <see cref="UpdateUserDto"/>, so a privilege cannot ride along with an email edit — and
    /// staff-only like everything else here, inherited from the controller.
    /// </summary>
    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<UserDetailDto>> ChangeRole(
        Guid id, ChangeRoleDto dto, CancellationToken ct)
    {
        // Read first, for one reason: the log line below has to name what the role *was*, and the
        // service returns only what it is now. A refusal throws before anything is written, so the
        // extra read costs a query on a staff endpoint and buys an auditable sentence.
        var before = await users.GetAsync(id, ct);
        var actor = ActingUserId();
        var after = await users.ChangeRoleAsync(id, dto.Role, actor, ct);

        // The durable record is the log, deliberately — no table and no column (P1T-232).
        logger.LogInformation(
            "Role change: {Subject} {From} → {To}, by {Actor}.", id, before.Role, after.Role, actor);
        return Ok(after);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Who is calling, from the session token and nowhere else — the pattern
    /// <c>AccountController</c> uses. An ambient current-user service would make "can this person
    /// do this to themselves?" a question the Application layer answers from hidden state.
    /// </summary>
    private Guid ActingUserId() =>
        SessionRevocation.UserId(User)
        ?? throw new ConflictException("This session does not name an account.");
}
