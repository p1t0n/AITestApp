using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

public record SaveAchievementDto(int Order, string Text);

public record SaveExperienceDto(
    string Company,
    string Title,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Summary,
    IReadOnlyList<SaveAchievementDto> Achievements,
    IReadOnlyList<Guid> SkillIds);

public interface IExperienceService
{
    Task<ExperienceDto> AddAsync(Guid expertId, SaveExperienceDto dto, CancellationToken ct = default);
    Task<ExperienceDto> UpdateAsync(Guid experienceId, SaveExperienceDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid experienceId, CancellationToken ct = default);
}

public class ExperienceService : IExperienceService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveExperienceDto> _validator;
    private readonly IOwnershipScopeProvider _scope;
    public ExperienceService(
        IAppDbContext db, IValidator<SaveExperienceDto> validator, IOwnershipScopeProvider scope)
    {
        _db = db;
        _validator = validator;
        _scope = scope;
    }

    public async Task<ExperienceDto> AddAsync(Guid expertId, SaveExperienceDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        // Out of scope reads as "no such expert", not as "not yours": a 403 would confirm the id.
        if (!await _db.Experts.AnyAsync(e => e.Id == expertId && (unrestricted || e.Id == owned), ct))
            throw new NotFoundException(nameof(Expert), expertId);
        await ValidateSkillsAsync(dto.SkillIds, ct);

        var x = new Experience { Id = Guid.NewGuid(), ExpertId = expertId };
        ApplyScalars(x, dto);
        ReplaceChildren(x, dto);
        _db.Experiences.Add(x);
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(x.Id, ct);
    }

    public async Task<ExperienceDto> UpdateAsync(Guid experienceId, SaveExperienceDto dto, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var x = await _db.Experiences
            .Include(e => e.Achievements)
            .Include(e => e.Skills)
            .FirstOrDefaultAsync(e => e.Id == experienceId && (unrestricted || e.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(Experience), experienceId);
        await _validator.ValidateAndThrowAsync(dto, ct);
        await ValidateSkillsAsync(dto.SkillIds, ct);

        ApplyScalars(x, dto);
        x.Achievements.Clear();
        x.Skills.Clear();
        ReplaceChildren(x, dto);
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(x.Id, ct);
    }

    public async Task DeleteAsync(Guid experienceId, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var x = await _db.Experiences
            .FirstOrDefaultAsync(e => e.Id == experienceId && (unrestricted || e.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(Experience), experienceId);
        _db.Experiences.Remove(x);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ValidateSkillsAsync(IReadOnlyList<Guid> skillIds, CancellationToken ct)
    {
        if (skillIds.Count == 0) return;
        var distinct = skillIds.Distinct().ToList();
        var found = await _db.Skills.CountAsync(s => distinct.Contains(s.Id), ct);
        if (found != distinct.Count)
            throw new NotFoundException(nameof(Skill), "one or more skill ids");
    }

    private static void ApplyScalars(Experience x, SaveExperienceDto d)
    {
        x.Company = d.Company.Trim();
        x.Title = d.Title.Trim();
        x.Location = d.Location;
        x.StartDate = d.StartDate;
        x.EndDate = d.EndDate;
        x.Summary = d.Summary;
    }

    private static void ReplaceChildren(Experience x, SaveExperienceDto d)
    {
        // Ids stay unset: on the update path the children reach the change tracker via navigation
        // fixup, and EF marks a discovered entity with a pre-set key as Modified — an UPDATE
        // against a row that doesn't exist (DbUpdateConcurrencyException). An unset key tracks as
        // Added, and EF client-generates the Guid on both the create and update paths.
        foreach (var a in d.Achievements.OrderBy(a => a.Order))
            x.Achievements.Add(new Achievement { Order = a.Order, Text = a.Text.Trim() });
        foreach (var sid in d.SkillIds.Distinct())
            x.Skills.Add(new ExperienceSkill { SkillId = sid });
    }

    private async Task<ExperienceDto> ProjectAsync(Guid id, CancellationToken ct)
    {
        var x = await _db.Experiences.AsNoTracking()
            .Include(e => e.Achievements)
            .Include(e => e.Skills).ThenInclude(s => s.Skill)
            .FirstAsync(e => e.Id == id, ct);
        return x.ToDto();
    }
}

public class SaveExperienceValidator : AbstractValidator<SaveExperienceDto>
{
    public SaveExperienceValidator()
    {
        RuleFor(x => x.Company).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleForEach(x => x.Achievements).ChildRules(a =>
            a.RuleFor(y => y.Text).NotEmpty().MaximumLength(1000));
    }
}
