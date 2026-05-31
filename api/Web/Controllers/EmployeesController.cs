using EmployeeManager.Application.Cv;
using EmployeeManager.Application.Employees;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeManager.Web.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly ICvService _cv;

    public EmployeesController(IEmployeeService employees, ICvService cv)
    {
        _employees = employees;
        _cv = cv;
    }

    [HttpGet]
    public Task<IReadOnlyList<EmployeeSummaryDto>> List(CancellationToken ct) => _employees.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<EmployeeDetailDto> Get(Guid id, CancellationToken ct) => _employees.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<EmployeeDetailDto>> Create(SaveEmployeeDto dto, CancellationToken ct)
    {
        var created = await _employees.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<EmployeeDetailDto> Update(Guid id, SaveEmployeeDto dto, CancellationToken ct) =>
        _employees.UpdateAsync(id, dto, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _employees.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/cv")]
    public Task<CvDto> GetCv(Guid id, CancellationToken ct) => _cv.BuildAsync(id, ct);
}
