using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

public interface IAchievementService
{
    Task<AchievementDto> AddAsync(Guid experienceId, SaveAchievementDto dto, CancellationToken ct = default);
    Task<AchievementDto> UpdateAsync(Guid achievementId, SaveAchievementDto dto, CancellationToken ct = default);
    /// <summary>Rewrites one bullet's text in place — id and order untouched. The single-bullet
    /// seam the tailoring Apply flow needs (P1T-90): no read-modify-write of the whole experience,
    /// no regenerated sibling ids, no lost-update race.</summary>
    Task<AchievementDto> PatchTextAsync(Guid achievementId, string text, CancellationToken ct = default);
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

    public async Task<AchievementDto> PatchTextAsync(Guid achievementId, string text, CancellationToken ct = default)
    {
        var a = await _db.Achievements.FirstOrDefaultAsync(x => x.Id == achievementId, ct)
            ?? throw new NotFoundException(nameof(Achievement), achievementId);

        // Same text rules as a full save, with the existing order standing in for the unchanged field.
        await _validator.ValidateAndThrowAsync(new SaveAchievementDto(a.Order, text), ct);

        a.Text = text.Trim();
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
