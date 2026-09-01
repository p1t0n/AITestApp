using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Skills;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// The skill catalog. Reads are open to both audiences (P1T-182): an Expert editing their own CV
/// picks skills from the same catalog, and a category name is not personal data. Writes stay with
/// staff — the catalog is shared vocabulary, and one person renaming a category rewrites everyone's
/// CV. The class-level policy is the floor; each write re-declares the narrower one, and both
/// policies then have to pass.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.AnyRole)]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly ISkillCatalogService _catalog;
    public CatalogController(ISkillCatalogService catalog) => _catalog = catalog;

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => _catalog.ListCategoriesAsync(ct);

    [HttpGet("categories/tree")]
    public Task<IReadOnlyList<CategoryNodeDto>> Tree(CancellationToken ct) => _catalog.GetTreeAsync(ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("categories")]
    public Task<CategoryDto> CreateCategory(SaveCategoryDto dto, CancellationToken ct) =>
        _catalog.CreateCategoryAsync(dto, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPut("categories/{id:guid}")]
    public Task<CategoryDto> UpdateCategory(Guid id, SaveCategoryDto dto, CancellationToken ct) =>
        _catalog.UpdateCategoryAsync(id, dto, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await _catalog.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpGet("skills")]
    public Task<IReadOnlyList<SkillDto>> Skills(CancellationToken ct) => _catalog.ListSkillsAsync(ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPost("skills")]
    public Task<SkillDto> CreateSkill(SaveSkillDto dto, CancellationToken ct) => _catalog.CreateSkillAsync(dto, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpPut("skills/{id:guid}")]
    public Task<SkillDto> UpdateSkill(Guid id, SaveSkillDto dto, CancellationToken ct) =>
        _catalog.UpdateSkillAsync(id, dto, ct);

    [Authorize(Policy = AuthPolicies.ServiceManager)]
    [HttpDelete("skills/{id:guid}")]
    public async Task<IActionResult> DeleteSkill(Guid id, CancellationToken ct)
    {
        await _catalog.DeleteSkillAsync(id, ct);
        return NoContent();
    }
}
