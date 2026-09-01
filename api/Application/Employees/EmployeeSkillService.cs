using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Employees;

public record SaveEmployeeSkillDto(Guid SkillId, SkillLevel Level, decimal YearsExperience);

public interface IEmployeeSkillService
{
    Task<EmployeeSkillDto> AddAsync(Guid employeeId, SaveEmployeeSkillDto dto, CancellationToken ct = default);
    Task<EmployeeSkillDto> UpdateAsync(Guid employeeSkillId, SaveEmployeeSkillDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid employeeSkillId, CancellationToken ct = default);
}

public class EmployeeSkillService : IEmployeeSkillService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveEmployeeSkillDto> _validator;
    public EmployeeSkillService(IAppDbContext db, IValidator<SaveEmployeeSkillDto> validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<EmployeeSkillDto> AddAsync(Guid employeeId, SaveEmployeeSkillDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            throw new NotFoundException(nameof(Employee), employeeId);
        if (!await _db.Skills.AnyAsync(s => s.Id == dto.SkillId, ct))
            throw new NotFoundException(nameof(Skill), dto.SkillId);
        if (await _db.EmployeeSkills.AnyAsync(x => x.EmployeeId == employeeId && x.SkillId == dto.SkillId, ct))
            throw new ConflictException("Employee already has this skill.");

        var es = new EmployeeSkill
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            SkillId = dto.SkillId,
            Level = dto.Level,
            YearsExperience = dto.YearsExperience,
        };
        _db.EmployeeSkills.Add(es);
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(es.Id, ct);
    }

    public async Task<EmployeeSkillDto> UpdateAsync(Guid employeeSkillId, SaveEmployeeSkillDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var es = await _db.EmployeeSkills.FirstOrDefaultAsync(x => x.Id == employeeSkillId, ct)
            ?? throw new NotFoundException(nameof(EmployeeSkill), employeeSkillId);
        es.Level = dto.Level;
        es.YearsExperience = dto.YearsExperience;
        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(es.Id, ct);
    }

    public async Task DeleteAsync(Guid employeeSkillId, CancellationToken ct = default)
    {
        var es = await _db.EmployeeSkills.FirstOrDefaultAsync(x => x.Id == employeeSkillId, ct)
            ?? throw new NotFoundException(nameof(EmployeeSkill), employeeSkillId);
        _db.EmployeeSkills.Remove(es);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<EmployeeSkillDto> ProjectAsync(Guid id, CancellationToken ct) =>
        await _db.EmployeeSkills.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new EmployeeSkillDto(
                x.Id, x.SkillId, x.Skill.Name, x.Skill.Category.Name, x.Level, x.YearsExperience))
            .FirstAsync(ct);
}

public class SaveEmployeeSkillValidator : AbstractValidator<SaveEmployeeSkillDto>
{
    public SaveEmployeeSkillValidator()
    {
        RuleFor(x => x.SkillId).NotEmpty();
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.YearsExperience).InclusiveBetween(0, 80);
    }
}
