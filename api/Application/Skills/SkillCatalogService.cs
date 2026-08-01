using CvManager.Application.Abstractions;
using CvManager.Application.Common;
using CvManager.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvManager.Application.Skills;

public interface ISkillCatalogService
{
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CategoryNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateCategoryAsync(SaveCategoryDto dto, CancellationToken ct = default);
    Task<CategoryDto> UpdateCategoryAsync(Guid id, SaveCategoryDto dto, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SkillDto>> ListSkillsAsync(CancellationToken ct = default);
    Task<SkillDto> CreateSkillAsync(SaveSkillDto dto, CancellationToken ct = default);
    Task<SkillDto> UpdateSkillAsync(Guid id, SaveSkillDto dto, CancellationToken ct = default);
    Task DeleteSkillAsync(Guid id, CancellationToken ct = default);
}

public class SkillCatalogService : ISkillCatalogService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveCategoryDto> _categoryValidator;
    private readonly IValidator<SaveSkillDto> _skillValidator;
    public SkillCatalogService(
        IAppDbContext db,
        IValidator<SaveCategoryDto> categoryValidator,
        IValidator<SaveSkillDto> skillValidator)
    {
        _db = db;
        _categoryValidator = categoryValidator;
        _skillValidator = skillValidator;
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default) =>
        await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.ParentId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var categories = await _db.Categories.AsNoTracking().ToListAsync(ct);
        var skills = await _db.Skills.AsNoTracking().ToListAsync(ct);

        var skillsByCat = skills
            .GroupBy(s => s.CategoryId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Rank).ThenBy(s => s.Name).ToList());
        var byParent = categories
            .GroupBy(c => c.ParentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Name).ToList());

        List<CategoryNodeDto> Build(Guid? parentId) =>
            (byParent.TryGetValue(parentId ?? Guid.Empty, out var nodes) ? nodes : new())
            .Select(c => new CategoryNodeDto(
                c.Id, c.Name,
                Build(c.Id),
                (skillsByCat.TryGetValue(c.Id, out var ss) ? ss : new())
                    .Select(s => new SkillDto(s.Id, s.Name, s.CategoryId, c.Name, s.Rank)).ToList()))
            .ToList();

        return Build(null);
    }

    public async Task<CategoryDto> CreateCategoryAsync(SaveCategoryDto dto, CancellationToken ct = default)
    {
        await _categoryValidator.ValidateAndThrowAsync(dto, ct);
        var name = dto.Name.Trim();
        if (dto.ParentId is { } pid && !await _db.Categories.AnyAsync(c => c.Id == pid, ct))
            throw new NotFoundException(nameof(Category), pid);
        await EnsureCategoryNameFreeAsync(name, dto.ParentId, Guid.Empty, ct);

        var c = new Category { Id = Guid.NewGuid(), Name = name, ParentId = dto.ParentId };
        _db.Categories.Add(c);
        await _db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.ParentId);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, SaveCategoryDto dto, CancellationToken ct = default)
    {
        await _categoryValidator.ValidateAndThrowAsync(dto, ct);
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Category), id);

        var name = dto.Name.Trim();
        if (dto.ParentId is { } pid)
        {
            if (!await _db.Categories.AnyAsync(x => x.Id == pid, ct))
                throw new NotFoundException(nameof(Category), pid);
            await EnsureNoCycleAsync(id, pid, ct);
        }
        await EnsureCategoryNameFreeAsync(name, dto.ParentId, id, ct);

        c.Name = name;
        c.ParentId = dto.ParentId;
        await _db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.ParentId);
    }

    // Rejects setting a category's parent to itself or to one of its own descendants.
    private async Task EnsureNoCycleAsync(Guid id, Guid newParentId, CancellationToken ct)
    {
        var parents = await _db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);

        Guid? cursor = newParentId;
        while (cursor is { } cur)
        {
            if (cur == id)
                throw new ConflictException("Cannot move a category under itself or its own descendant.");
            cursor = parents.TryGetValue(cur, out var p) ? p : null;
        }
    }

    private async Task EnsureCategoryNameFreeAsync(string name, Guid? parentId, Guid excludeId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await _db.Categories.AnyAsync(
                c => c.ParentId == parentId && c.Id != excludeId && c.Name.ToLower() == lower, ct))
            throw new ConflictException($"A category named \"{name}\" already exists here.");
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Category), id);

        if (await _db.Categories.AnyAsync(x => x.ParentId == id, ct) ||
            await _db.Skills.AnyAsync(x => x.CategoryId == id, ct))
            throw new ConflictException("Category has child categories or skills and cannot be deleted.");

        _db.Categories.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SkillDto>> ListSkillsAsync(CancellationToken ct = default) =>
        await _db.Skills.AsNoTracking()
            .OrderByDescending(s => s.Rank).ThenBy(s => s.Name)
            .Select(s => new SkillDto(s.Id, s.Name, s.CategoryId, s.Category.Name, s.Rank))
            .ToListAsync(ct);

    public async Task<SkillDto> CreateSkillAsync(SaveSkillDto dto, CancellationToken ct = default)
    {
        await _skillValidator.ValidateAndThrowAsync(dto, ct);
        var name = dto.Name.Trim();
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId, ct)
            ?? throw new NotFoundException(nameof(Category), dto.CategoryId);
        await EnsureSkillNameFreeAsync(name, dto.CategoryId, Guid.Empty, ct);

        var s = new Skill { Id = Guid.NewGuid(), Name = name, CategoryId = dto.CategoryId };
        _db.Skills.Add(s);
        await _db.SaveChangesAsync(ct);
        return new SkillDto(s.Id, s.Name, s.CategoryId, cat.Name, s.Rank);
    }

    public async Task<SkillDto> UpdateSkillAsync(Guid id, SaveSkillDto dto, CancellationToken ct = default)
    {
        await _skillValidator.ValidateAndThrowAsync(dto, ct);
        var s = await _db.Skills.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Skill), id);

        var name = dto.Name.Trim();
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId, ct)
            ?? throw new NotFoundException(nameof(Category), dto.CategoryId);
        await EnsureSkillNameFreeAsync(name, dto.CategoryId, id, ct);

        // Rank is left untouched here — it is owned by the ranking calculation, not this edit.
        s.Name = name;
        s.CategoryId = dto.CategoryId;
        await _db.SaveChangesAsync(ct);
        return new SkillDto(s.Id, s.Name, s.CategoryId, cat.Name, s.Rank);
    }

    private async Task EnsureSkillNameFreeAsync(string name, Guid categoryId, Guid excludeId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await _db.Skills.AnyAsync(
                s => s.CategoryId == categoryId && s.Id != excludeId && s.Name.ToLower() == lower, ct))
            throw new ConflictException($"A skill named \"{name}\" already exists in this category.");
    }

    public async Task DeleteSkillAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.Skills.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Skill), id);

        if (await _db.EmployeeSkills.AnyAsync(x => x.SkillId == id, ct) ||
            await _db.ExperienceSkills.AnyAsync(x => x.SkillId == id, ct))
            throw new ConflictException("Skill is in use by employees or experiences and cannot be deleted.");

        _db.Skills.Remove(s);
        await _db.SaveChangesAsync(ct);
    }
}
