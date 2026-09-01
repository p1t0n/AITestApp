using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

public record SaveExpertSkillDto(Guid SkillId, SkillLevel Level, decimal YearsExperience);

public interface IExpertSkillService
{
    Task<ExpertSkillDto> AddAsync(Guid expertId, SaveExpertSkillDto dto, CancellationToken ct = default);
    Task<ExpertSkillDto> UpdateAsync(Guid expertSkillId, SaveExpertSkillDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid expertSkillId, CancellationToken ct = default);
}

public class ExpertSkillService : IExpertSkillService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveExpertSkillDto> _validator;
    public ExpertSkillService(IAppDbContext db, IValidator<SaveExpertSkillDto> validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<ExpertSkillDto> AddAsync(Guid expertId, SaveExpertSkillDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        if (!await _db.Experts.AnyAsync(e => e.Id == expertId, ct))
            throw new NotFoundException(nameof(Expert), expertId);
        if (!await _db.Skills.AnyAsync(s => s.Id == dto.SkillId, ct))
            throw new NotFoundException(nameof(Skill), dto.SkillId);
        if (await _db.ExpertSkills.AnyAsync(x => x.ExpertId == expertId && x.SkillId == dto.SkillId, ct))
            throw new ConflictException("Expert already has this skill.");

        var es = new ExpertSkill
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            SkillId = dto.SkillId,
            Level = dto.Level,
            YearsExperience = dto.YearsExperience,
        };
        _db.ExpertSkills.Add(es);
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(es.Id, ct);
    }

    public async Task<ExpertSkillDto> UpdateAsync(Guid expertSkillId, SaveExpertSkillDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var es = await _db.ExpertSkills.FirstOrDefaultAsync(x => x.Id == expertSkillId, ct)
            ?? throw new NotFoundException(nameof(ExpertSkill), expertSkillId);
        es.Level = dto.Level;
        es.YearsExperience = dto.YearsExperience;
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(es.Id, ct);
    }

    public async Task DeleteAsync(Guid expertSkillId, CancellationToken ct = default)
    {
        var es = await _db.ExpertSkills.FirstOrDefaultAsync(x => x.Id == expertSkillId, ct)
            ?? throw new NotFoundException(nameof(ExpertSkill), expertSkillId);
        _db.ExpertSkills.Remove(es);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ExpertSkillDto> ProjectAsync(Guid id, CancellationToken ct) =>
        await _db.ExpertSkills.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ExpertSkillDto(
                x.Id, x.SkillId, x.Skill.Name, x.Skill.Category.Name, x.Level, x.YearsExperience))
            .FirstAsync(ct);
}

public class SaveExpertSkillValidator : AbstractValidator<SaveExpertSkillDto>
{
    public SaveExpertSkillValidator()
    {
        RuleFor(x => x.SkillId).NotEmpty();
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.YearsExperience).InclusiveBetween(0, 80);
    }
}
