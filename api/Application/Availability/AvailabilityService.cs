using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Availability;

public record SaveAvailabilityEntryDto(DateOnly EffectiveFrom, int CapacityPercent);

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilityEntryDto>> ListAsync(Guid expertId, CancellationToken ct = default);
    Task<AvailabilityEntryDto> AddAsync(Guid expertId, SaveAvailabilityEntryDto dto, CancellationToken ct = default);
    Task<AvailabilityEntryDto> UpdateAsync(Guid entryId, SaveAvailabilityEntryDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid entryId, CancellationToken ct = default);
}

public class AvailabilityService : IAvailabilityService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveAvailabilityEntryDto> _validator;
    private readonly IOwnershipScopeProvider _scope;
    public AvailabilityService(
        IAppDbContext db, IValidator<SaveAvailabilityEntryDto> validator, IOwnershipScopeProvider scope)
    {
        _db = db;
        _validator = validator;
        _scope = scope;
    }

    public async Task<IReadOnlyList<AvailabilityEntryDto>> ListAsync(Guid expertId, CancellationToken ct = default)
    {
        // Not "empty list": someone else's calendar answers exactly as a non-existent expert does.
        // An empty list would be a different answer from a 404, and the difference is the leak — it
        // would tell an Expert which ids on the roster are real.
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        if (!await _db.Experts.AnyAsync(e => e.Id == expertId && (unrestricted || e.Id == owned), ct))
            throw new NotFoundException(nameof(Expert), expertId);

        return await _db.AvailabilityEntries.AsNoTracking()
            .Where(a => a.ExpertId == expertId)
            .OrderBy(a => a.EffectiveFrom)
            .Select(a => new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent))
            .ToListAsync(ct);
    }

    public async Task<AvailabilityEntryDto> AddAsync(Guid expertId, SaveAvailabilityEntryDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        // Out of scope reads as "no such expert", not as "not yours": a 403 would confirm the id.
        if (!await _db.Experts.AnyAsync(e => e.Id == expertId && (unrestricted || e.Id == owned), ct))
            throw new NotFoundException(nameof(Expert), expertId);
        if (await _db.AvailabilityEntries.AnyAsync(a => a.ExpertId == expertId && a.EffectiveFrom == dto.EffectiveFrom, ct))
            throw new ConflictException($"An availability entry already exists for {dto.EffectiveFrom}.");

        var a = new AvailabilityEntry
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            EffectiveFrom = dto.EffectiveFrom,
            CapacityPercent = dto.CapacityPercent,
        };
        _db.AvailabilityEntries.Add(a);
        await _db.SaveChangesAsync(ct);
        return new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent);
    }

    public async Task<AvailabilityEntryDto> UpdateAsync(Guid entryId, SaveAvailabilityEntryDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var a = await _db.AvailabilityEntries
            .FirstOrDefaultAsync(x => x.Id == entryId && (unrestricted || x.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(AvailabilityEntry), entryId);
        if (a.EffectiveFrom != dto.EffectiveFrom &&
            await _db.AvailabilityEntries.AnyAsync(x => x.ExpertId == a.ExpertId && x.EffectiveFrom == dto.EffectiveFrom, ct))
            throw new ConflictException($"An availability entry already exists for {dto.EffectiveFrom}.");

        a.EffectiveFrom = dto.EffectiveFrom;
        a.CapacityPercent = dto.CapacityPercent;
        await _db.SaveChangesAsync(ct);
        return new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent);
    }

    public async Task DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var a = await _db.AvailabilityEntries
            .FirstOrDefaultAsync(x => x.Id == entryId && (unrestricted || x.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(AvailabilityEntry), entryId);
        _db.AvailabilityEntries.Remove(a);
        await _db.SaveChangesAsync(ct);
    }
}

public class SaveAvailabilityEntryValidator : AbstractValidator<SaveAvailabilityEntryDto>
{
    public SaveAvailabilityEntryValidator()
    {
        RuleFor(x => x.CapacityPercent).InclusiveBetween(0, 100);
    }
}
