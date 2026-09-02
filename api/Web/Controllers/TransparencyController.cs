using System.Text;
using System.Text.Json;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// What we hold on you, and a copy of it (P1T-187) — Art. 15 access and Art. 20 portability.
///
/// <para>Two endpoints rather than one with a flag, because they owe opposite things: the access
/// view must <em>include</em> what software worked out about the person, and the portable copy must
/// <em>exclude</em> it. A shared endpoint would eventually get one of the two wrong.</para>
///
/// <para>Under <c>api/me</c> with no id, like the pause and the erasure beside it. The Service
/// Manager's on-behalf export lives on the roster controller instead, because it is an act on
/// somebody else's record and it leaves a record of its own.</para>
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/me")]
public class TransparencyController(
    IAccessAndExportService transparency, IOwnershipScopeProvider scope) : ControllerBase
{
    /// <summary>Everything Art. 15 owes about the caller's own record, derived data included.</summary>
    [HttpGet("access")]
    public async Task<ActionResult<AccessViewDto>> Access(CancellationToken ct) =>
        Ok(await transparency.AccessAsync(await MyExpertIdAsync(ct), ct));

    /// <summary>
    /// The portable copy, as a JSON file the browser saves. Synchronous: this is one person's own
    /// record, bounded and small, so there is no job to queue, no "ready shortly" state to design,
    /// and — since the service sends no email — no link to deliver afterwards anyway.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var expertId = await MyExpertIdAsync(ct);
        return File(Serialize(await transparency.ExportAsync(expertId, ct)),
            "application/json", $"experttojob-export-{expertId}.json");
    }

    /// <summary>
    /// The record the caller owns. Resolved from the ownership scope rather than from a route id:
    /// an Expert reaches their own and nothing else, and somebody whose claim is still waiting owns
    /// nothing — which answers 404 here exactly as it does everywhere else (P1T-182).
    /// </summary>
    private async Task<Guid> MyExpertIdAsync(CancellationToken ct)
    {
        var current = await scope.CurrentAsync(ct);
        return current.ExpertId
               ?? throw new NotFoundException("Expert", SessionRevocation.UserId(User) ?? Guid.Empty);
    }

    /// <summary>Indented on purpose: Art. 20 asks for machine-readable, and a person opening the
    /// file to check what they were given is the first reader either way.</summary>
    internal static byte[] Serialize<T>(T payload) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, ExportJson));

    internal static readonly JsonSerializerOptions ExportJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
