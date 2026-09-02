using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// Erasure (P1T-186): a person deleting themselves, account and roster row together.
///
/// <para>Under <c>api/me</c> and carrying no id, like the pause control beside it — the row erased
/// is always the caller's own, and there is no route that names anybody else's. The two are
/// deliberately different kinds of act and are kept apart on the page as well (P1T-171): with no
/// email, somebody who deleted when they meant to pause cannot be reached to undo it.</para>
///
/// <para>A <c>POST</c> to a named action rather than <c>DELETE /api/me/account</c>, for two
/// reasons: the control word has to travel in a body, and an irreversible act reads better with a
/// verb on it than as a method somebody could send by reflex.</para>
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/me/account")]
public class AccountController(IErasureService erasure) : ControllerBase
{
    /// <summary>
    /// Deletes the caller's account and roster row. Synchronous and irreversible; the session it
    /// was called with stops working immediately, on this host and on the Agents service, because
    /// the account both of them re-read every request is gone.
    /// </summary>
    [HttpPost("erase")]
    public async Task<ActionResult<ErasureResult>> Erase(EraseAccountRequest request, CancellationToken ct) =>
        Ok(await erasure.EraseMineAsync(ActingUserId(), request.ControlWord, ct));

    private Guid ActingUserId() =>
        SessionRevocation.UserId(User)
        ?? throw new ConflictException("This session does not name an account.");
}

/// <param name="ControlWord">The account's control word, re-entered. The only proof-of-person this
/// service has, which is what makes an irreversible act more than a misclick.</param>
public sealed record EraseAccountRequest(string ControlWord);
