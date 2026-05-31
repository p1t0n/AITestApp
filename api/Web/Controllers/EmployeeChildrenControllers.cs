using EmployeeManager.Application.Availability;
using EmployeeManager.Application.Employees;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeManager.Web.Controllers;

[ApiController]
public class LanguagesController : ControllerBase
{
    private readonly ILanguageService _svc;
    public LanguagesController(ILanguageService svc) => _svc = svc;

    [HttpPost("api/employees/{employeeId:guid}/languages")]
    public Task<SpokenLanguageDto> Add(Guid employeeId, SaveSpokenLanguageDto dto, CancellationToken ct) =>
        _svc.AddAsync(employeeId, dto, ct);

    [HttpPut("api/languages/{id:guid}")]
    public Task<SpokenLanguageDto> Update(Guid id, SaveSpokenLanguageDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/languages/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
public class AvailabilityController : ControllerBase
{
    private readonly IAvailabilityService _svc;
    public AvailabilityController(IAvailabilityService svc) => _svc = svc;

    [HttpGet("api/employees/{employeeId:guid}/availability")]
    public Task<IReadOnlyList<AvailabilityEntryDto>> List(Guid employeeId, CancellationToken ct) =>
        _svc.ListAsync(employeeId, ct);

    [HttpPost("api/employees/{employeeId:guid}/availability")]
    public Task<AvailabilityEntryDto> Add(Guid employeeId, SaveAvailabilityEntryDto dto, CancellationToken ct) =>
        _svc.AddAsync(employeeId, dto, ct);

    [HttpPut("api/availability/{id:guid}")]
    public Task<AvailabilityEntryDto> Update(Guid id, SaveAvailabilityEntryDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/availability/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
public class EmployeeSkillsController : ControllerBase
{
    private readonly IEmployeeSkillService _svc;
    public EmployeeSkillsController(IEmployeeSkillService svc) => _svc = svc;

    [HttpPost("api/employees/{employeeId:guid}/skills")]
    public Task<EmployeeSkillDto> Add(Guid employeeId, SaveEmployeeSkillDto dto, CancellationToken ct) =>
        _svc.AddAsync(employeeId, dto, ct);

    [HttpPut("api/employee-skills/{id:guid}")]
    public Task<EmployeeSkillDto> Update(Guid id, SaveEmployeeSkillDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/employee-skills/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
public class QualificationsController : ControllerBase
{
    private readonly IQualificationService _svc;
    public QualificationsController(IQualificationService svc) => _svc = svc;

    [HttpPost("api/employees/{employeeId:guid}/qualifications")]
    public Task<QualificationDto> Add(Guid employeeId, SaveQualificationDto dto, CancellationToken ct) =>
        _svc.AddAsync(employeeId, dto, ct);

    [HttpPut("api/qualifications/{id:guid}")]
    public Task<QualificationDto> Update(Guid id, SaveQualificationDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/qualifications/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
public class ExperiencesController : ControllerBase
{
    private readonly IExperienceService _svc;
    public ExperiencesController(IExperienceService svc) => _svc = svc;

    [HttpPost("api/employees/{employeeId:guid}/experiences")]
    public Task<ExperienceDto> Add(Guid employeeId, SaveExperienceDto dto, CancellationToken ct) =>
        _svc.AddAsync(employeeId, dto, ct);

    [HttpPut("api/experiences/{id:guid}")]
    public Task<ExperienceDto> Update(Guid id, SaveExperienceDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/experiences/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}
