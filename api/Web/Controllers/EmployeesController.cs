using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Employees;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly ICvService _cv;
    private readonly ICvPdfRenderer _pdf;

    public EmployeesController(IEmployeeService employees, ICvService cv, ICvPdfRenderer pdf)
    {
        _employees = employees;
        _cv = cv;
        _pdf = pdf;
    }

    [HttpGet]
    public Task<IReadOnlyList<EmployeeSummaryDto>> List(
        [FromQuery] bool includeDrafts, CancellationToken ct) => _employees.ListAsync(includeDrafts, ct);

    [HttpGet("{id:guid}")]
    public Task<EmployeeDetailDto> Get(Guid id, CancellationToken ct) => _employees.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<EmployeeDetailDto>> Create(SaveEmployeeDto dto, CancellationToken ct)
    {
        var created = await _employees.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>The human publication gate for agent-staged drafts (P1T-92): flips Draft → Active.
    /// Deliberately a Web API action (user session), never an MCP tool — humans hold write authority.</summary>
    [HttpPost("{id:guid}/promote")]
    public Task<EmployeeDetailDto> Promote(Guid id, CancellationToken ct) => _employees.PromoteAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<EmployeeDetailDto> Update(Guid id, SaveEmployeeDto dto, CancellationToken ct) =>
        _employees.UpdateAsync(id, dto, ct);

    /// <summary>Partial update — only the fields present in <paramref name="dto"/> change (P1T-137).
    /// PUT above keeps full-replace semantics; this is the patch-shaped sibling.</summary>
    [HttpPatch("{id:guid}")]
    public Task<EmployeeDetailDto> Patch(Guid id, UpdateEmployeeDto dto, CancellationToken ct) =>
        _employees.PatchAsync(id, dto, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _employees.DeleteAsync(id, ct);
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
