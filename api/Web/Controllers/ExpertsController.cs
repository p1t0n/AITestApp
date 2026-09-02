using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// The roster. Staff-only apart from two actions: an Expert may read and save <em>their own</em>
/// row (P1T-182), and which row that is comes from the ownership scope in the Application layer, not
/// from the id in the URL — asking for someone else's is a 404, indistinguishable from a row that
/// does not exist. <c>GET /api/experts</c> is the one endpoint that would hand over the whole
/// product, so it stays staff-only and is scoped underneath as well.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/experts")]
public class ExpertsController : ControllerBase
{
    private readonly IExpertService _experts;
    private readonly ICvService _cv;
    private readonly ICvPdfRenderer _pdf;

    public ExpertsController(IExpertService experts, ICvService cv, ICvPdfRenderer pdf)
    {
        _experts = experts;
        _cv = cv;
        _pdf = pdf;
    }

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet]
    public Task<IReadOnlyList<ExpertSummaryDto>> List(
        [FromQuery] bool includeDrafts, CancellationToken ct) => _experts.ListAsync(includeDrafts, ct);

    [HttpGet("{id:guid}")]
    public Task<ExpertDetailDto> Get(Guid id, CancellationToken ct) => _experts.GetAsync(id, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost]
    public async Task<ActionResult<ExpertDetailDto>> Create(SaveExpertDto dto, CancellationToken ct)
    {
        var created = await _experts.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>The human publication gate for agent-staged drafts (P1T-92): flips Draft → Active.
    /// Deliberately a Web API action (user session), never an MCP tool — humans hold write authority.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("{id:guid}/promote")]
    public Task<ExpertDetailDto> Promote(Guid id, CancellationToken ct) => _experts.PromoteAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<ExpertDetailDto> Update(Guid id, SaveExpertDto dto, CancellationToken ct) =>
        _experts.UpdateAsync(id, dto, ct);

    /// <summary>Partial update — only the fields present in <paramref name="dto"/> change (P1T-137).
    /// PUT above keeps full-replace semantics; this is the patch-shaped sibling.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPatch("{id:guid}")]
    public Task<ExpertDetailDto> Patch(Guid id, UpdateExpertDto dto, CancellationToken ct) =>
        _experts.PatchAsync(id, dto, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _experts.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Takes a copy of one person's file on their behalf (P1T-187) — the phoned-in request, since
    /// this service has no email to receive one by. A <c>POST</c> because it is not a plain read:
    /// it writes a record of the staff member who did it. That record is about them, not about the
    /// Expert, and it is deliberately not a log of who viewed whom.
    /// </summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("{id:guid}/export")]
    public async Task<IActionResult> ExportOnBehalf(
        Guid id, IAccessAndExportService transparency, CancellationToken ct)
    {
        var actingUserId = SessionRevocation.UserId(User)
            ?? throw new ConflictException("This session does not name an account.");
        var export = await transparency.ExportOnBehalfAsync(id, actingUserId, ct);

        return File(TransparencyController.Serialize(export),
            "application/json", $"experttojob-export-{id}.json");
    }

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet("{id:guid}/cv")]
    public Task<CvDto> GetCv(Guid id, CancellationToken ct) => _cv.BuildAsync(id, ct);

    /// <summary>Server-side CV render (P1T-139) — the headless sibling of the SPA's browser print.
    /// Thin adapter: the CV projection and the renderer both live below the Web layer.</summary>
    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpGet("{id:guid}/cv.pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetCvPdf(Guid id, CancellationToken ct)
    {
        var cv = await _cv.BuildAsync(id, ct);
        return File(_pdf.Render(cv), "application/pdf", CvPdfFileName.For(cv));
    }
}
