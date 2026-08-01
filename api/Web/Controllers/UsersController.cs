using CvManager.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace CvManager.Web.Controllers;

/// <summary>
/// User management. Requires authentication (the app-wide fallback policy); roles are flat, so any
/// signed-in user may manage any user. Not exposed over MCP.
/// </summary>
[ApiController]
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
