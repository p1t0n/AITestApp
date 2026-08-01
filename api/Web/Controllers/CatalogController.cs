using CvManager.Application.Skills;
using Microsoft.AspNetCore.Mvc;

namespace CvManager.Web.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly ISkillCatalogService _catalog;
    public CatalogController(ISkillCatalogService catalog) => _catalog = catalog;

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => _catalog.ListCategoriesAsync(ct);

    [HttpGet("categories/tree")]
    public Task<IReadOnlyList<CategoryNodeDto>> Tree(CancellationToken ct) => _catalog.GetTreeAsync(ct);

    [HttpPost("categories")]
    public Task<CategoryDto> CreateCategory(SaveCategoryDto dto, CancellationToken ct) =>
        _catalog.CreateCategoryAsync(dto, ct);

    [HttpPut("categories/{id:guid}")]
    public Task<CategoryDto> UpdateCategory(Guid id, SaveCategoryDto dto, CancellationToken ct) =>
        _catalog.UpdateCategoryAsync(id, dto, ct);

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await _catalog.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpGet("skills")]
    public Task<IReadOnlyList<SkillDto>> Skills(CancellationToken ct) => _catalog.ListSkillsAsync(ct);

    [HttpPost("skills")]
    public Task<SkillDto> CreateSkill(SaveSkillDto dto, CancellationToken ct) => _catalog.CreateSkillAsync(dto, ct);

    [HttpPut("skills/{id:guid}")]
    public Task<SkillDto> UpdateSkill(Guid id, SaveSkillDto dto, CancellationToken ct) =>
        _catalog.UpdateSkillAsync(id, dto, ct);

    [HttpDelete("skills/{id:guid}")]
    public async Task<IActionResult> DeleteSkill(Guid id, CancellationToken ct)
    {
        await _catalog.DeleteSkillAsync(id, ct);
        return NoContent();
    }
}
