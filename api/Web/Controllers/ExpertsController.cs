using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.ServiceManager)]
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

    [HttpGet]
    public Task<IReadOnlyList<ExpertSummaryDto>> List(
        [FromQuery] bool includeDrafts, CancellationToken ct) => _experts.ListAsync(includeDrafts, ct);

    [HttpGet("{id:guid}")]
    public Task<ExpertDetailDto> Get(Guid id, CancellationToken ct) => _experts.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ExpertDetailDto>> Create(SaveExpertDto dto, CancellationToken ct)
    {
        var created = await _experts.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>The human publication gate for agent-staged drafts (P1T-92): flips Draft → Active.
    /// Deliberately a Web API action (user session), never an MCP tool — humans hold write authority.</summary>
    [HttpPost("{id:guid}/promote")]
    public Task<ExpertDetailDto> Promote(Guid id, CancellationToken ct) => _experts.PromoteAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<ExpertDetailDto> Update(Guid id, SaveExpertDto dto, CancellationToken ct) =>
        _experts.UpdateAsync(id, dto, ct);

    /// <summary>Partial update — only the fields present in <paramref name="dto"/> change (P1T-137).
    /// PUT above keeps full-replace semantics; this is the patch-shaped sibling.</summary>
    [HttpPatch("{id:guid}")]
    public Task<ExpertDetailDto> Patch(Guid id, UpdateExpertDto dto, CancellationToken ct) =>
        _experts.PatchAsync(id, dto, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _experts.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/cv")]
    public Task<CvDto> GetCv(Guid id, CancellationToken ct) => _cv.BuildAsync(id, ct);

    /// <summary>Server-side CV render (P1T-139) — the headless sibling of the SPA's browser print.
    /// Thin adapter: the CV projection and the renderer both live below the Web layer.</summary>
    [HttpGet("{id:guid}/cv.pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetCvPdf(Guid id, CancellationToken ct)
    {
        var cv = await _cv.BuildAsync(id, ct);
        return File(_pdf.Render(cv), "application/pdf", CvPdfFileName.For(cv));
    }
}
