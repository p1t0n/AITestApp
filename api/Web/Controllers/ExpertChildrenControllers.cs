using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Experts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// The seventeen child endpoints of a CV. Both audiences reach all of them (P1T-182) — an Expert
// edits their own languages, skills, jobs and bullets — and *which* rows they reach is decided one
// layer down by the ownership scope, not here. Eleven of these carry no expert in the URL at all
// (PUT /api/languages/{id}, PATCH /api/achievements/{id}), which is exactly why the check cannot
// live in the route.

namespace ExpertToJob.Web.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
public class LanguagesController : ControllerBase
{
    private readonly ILanguageService _svc;
    public LanguagesController(ILanguageService svc) => _svc = svc;

    [HttpPost("api/experts/{expertId:guid}/languages")]
    public Task<SpokenLanguageDto> Add(Guid expertId, SaveSpokenLanguageDto dto, CancellationToken ct) =>
        _svc.AddAsync(expertId, dto, ct);

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
[Authorize(Policy = AuthPolicies.AnyRole)]
public class AvailabilityController : ControllerBase
{
    private readonly IAvailabilityService _svc;
    public AvailabilityController(IAvailabilityService svc) => _svc = svc;

    [HttpGet("api/experts/{expertId:guid}/availability")]
    public Task<IReadOnlyList<AvailabilityEntryDto>> List(Guid expertId, CancellationToken ct) =>
        _svc.ListAsync(expertId, ct);

    [HttpPost("api/experts/{expertId:guid}/availability")]
    public Task<AvailabilityEntryDto> Add(Guid expertId, SaveAvailabilityEntryDto dto, CancellationToken ct) =>
        _svc.AddAsync(expertId, dto, ct);

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
[Authorize(Policy = AuthPolicies.AnyRole)]
public class ExpertSkillsController : ControllerBase
{
    private readonly IExpertSkillService _svc;
    public ExpertSkillsController(IExpertSkillService svc) => _svc = svc;

    [HttpPost("api/experts/{expertId:guid}/skills")]
    public Task<ExpertSkillDto> Add(Guid expertId, SaveExpertSkillDto dto, CancellationToken ct) =>
        _svc.AddAsync(expertId, dto, ct);

    [HttpPut("api/expert-skills/{id:guid}")]
    public Task<ExpertSkillDto> Update(Guid id, SaveExpertSkillDto dto, CancellationToken ct) =>
        _svc.UpdateAsync(id, dto, ct);

    [HttpDelete("api/expert-skills/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
public class QualificationsController : ControllerBase
{
    private readonly IQualificationService _svc;
    public QualificationsController(IQualificationService svc) => _svc = svc;

    [HttpPost("api/experts/{expertId:guid}/qualifications")]
    public Task<QualificationDto> Add(Guid expertId, SaveQualificationDto dto, CancellationToken ct) =>
        _svc.AddAsync(expertId, dto, ct);

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
[Authorize(Policy = AuthPolicies.AnyRole)]
public class AchievementsController : ControllerBase
{
    private readonly IAchievementService _svc;
    public AchievementsController(IAchievementService svc) => _svc = svc;

    public record PatchAchievementTextDto(string Text);

    /// <summary>Single-bullet rewrite (P1T-90): the tailoring Apply flow's seam. Text only —
    /// id and order are untouched, so sibling bullets and concurrent applies stay safe.</summary>
    [HttpPatch("api/achievements/{id:guid}")]
    public Task<AchievementDto> PatchText(Guid id, PatchAchievementTextDto dto, CancellationToken ct) =>
        _svc.PatchTextAsync(id, dto.Text, ct);
}

[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
public class ExperiencesController : ControllerBase
{
    private readonly IExperienceService _svc;
    public ExperiencesController(IExperienceService svc) => _svc = svc;

    [HttpPost("api/experts/{expertId:guid}/experiences")]
    public Task<ExperienceDto> Add(Guid expertId, SaveExperienceDto dto, CancellationToken ct) =>
        _svc.AddAsync(expertId, dto, ct);

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
