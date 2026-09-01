using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
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

/// <summary>
/// Achievements are two hops from their Expert — achievement → experience → expert — which is why
/// every ownership predicate here reads through the parent navigation. It is the longest path on the
/// roster and the easiest one to leave open: <c>PATCH /api/achievements/{id}</c> carries no expert
/// in the URL at all.
/// </summary>
public class AchievementService : IAchievementService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveAchievementDto> _validator;
    private readonly IOwnershipScopeProvider _scope;
    public AchievementService(
        IAppDbContext db, IValidator<SaveAchievementDto> validator, IOwnershipScopeProvider scope)
    {
        _db = db;
        _validator = validator;
        _scope = scope;
    }


    public async Task<AchievementDto> AddAsync(Guid experienceId, SaveAchievementDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        if (!await _db.Experiences.AnyAsync(
                e => e.Id == experienceId && (unrestricted || e.ExpertId == owned), ct))
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
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var a = await _db.Achievements
            .FirstOrDefaultAsync(
                x => x.Id == achievementId && (unrestricted || x.Experience.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(Achievement), achievementId);

        a.Order = dto.Order;
        a.Text = dto.Text.Trim();
        await _db.SaveChangesAsync(ct);
        return new AchievementDto(a.Id, a.Order, a.Text);
    }

    public async Task<AchievementDto> PatchTextAsync(Guid achievementId, string text, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var a = await _db.Achievements
            .FirstOrDefaultAsync(
                x => x.Id == achievementId && (unrestricted || x.Experience.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(Achievement), achievementId);

        // Same text rules as a full save, with the existing order standing in for the unchanged field.
        await _validator.ValidateAndThrowAsync(new SaveAchievementDto(a.Order, text), ct);

        a.Text = text.Trim();
        await _db.SaveChangesAsync(ct);
        return new AchievementDto(a.Id, a.Order, a.Text);
    }

    public async Task DeleteAsync(Guid achievementId, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var a = await _db.Achievements
            .FirstOrDefaultAsync(
                x => x.Id == achievementId && (unrestricted || x.Experience.ExpertId == owned), ct)
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
