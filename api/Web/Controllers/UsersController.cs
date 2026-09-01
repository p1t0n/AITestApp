using ExpertToJob.Application.Auth;
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
[Authorize(Policy = AuthPolicies.ServiceManager)]
[Route("api/users")]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserSummaryDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<UserDetailDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<UserDetailDto> Update(Guid id, UpdateUserDto dto, CancellationToken ct) =>
        users.UpdateAsync(id, dto, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }
}
