using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Common;
using EmployeeManager.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Application.Employees;

public interface IAchievementService
{
    Task<AchievementDto> AddAsync(Guid experienceId, SaveAchievementDto dto, CancellationToken ct = default);
    Task<AchievementDto> UpdateAsync(Guid achievementId, SaveAchievementDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid achievementId, CancellationToken ct = default);
}

public class AchievementService : IAchievementService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveAchievementDto> _validator;
    public AchievementService(IAppDbContext db, IValidator<SaveAchievementDto> validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<AchievementDto> AddAsync(Guid experienceId, SaveAchievementDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        if (!await _db.Experiences.AnyAsync(e => e.Id == experienceId, ct))
            throw new NotFoundException(nameof(Experience), experienceId);

        var a = new Achievement
        {
            Id = Guid.NewGuid(),
            ExperienceId = experienceId,
            Order = dto.Order,
            Text = dto.Text.Trim(),
        };
        _db.Achievements.Add(a);
        await _db.SaveChangesAsync(ct);
        return new AchievementDto(a.Id, a.Order, a.Text);
    }

    public async Task<AchievementDto> UpdateAsync(Guid achievementId, SaveAchievementDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var a = await _db.Achievements.FirstOrDefaultAsync(x => x.Id == achievementId, ct)
            ?? throw new NotFoundException(nameof(Achievement), achievementId);

        a.Order = dto.Order;
        a.Text = dto.Text.Trim();
        await _db.SaveChangesAsync(ct);
        return new AchievementDto(a.Id, a.Order, a.Text);
    }

    public async Task DeleteAsync(Guid achievementId, CancellationToken ct = default)
    {
        var a = await _db.Achievements.FirstOrDefaultAsync(x => x.Id == achievementId, ct)
            ?? throw new NotFoundException(nameof(Achievement), achievementId);
        _db.Achievements.Remove(a);
        await _db.SaveChangesAsync(ct);
    }
}

public class SaveAchievementValidator : AbstractValidator<SaveAchievementDto>
{
    public SaveAchievementValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
    }
}
