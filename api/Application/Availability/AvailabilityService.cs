using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Common;
using EmployeeManager.Application.Employees;
using EmployeeManager.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Application.Availability;

public record SaveAvailabilityEntryDto(DateOnly EffectiveFrom, int CapacityPercent);

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilityEntryDto>> ListAsync(Guid employeeId, CancellationToken ct = default);
    Task<AvailabilityEntryDto> AddAsync(Guid employeeId, SaveAvailabilityEntryDto dto, CancellationToken ct = default);
    Task<AvailabilityEntryDto> UpdateAsync(Guid entryId, SaveAvailabilityEntryDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid entryId, CancellationToken ct = default);
}

public class AvailabilityService : IAvailabilityService
{
    private readonly IAppDbContext _db;
    public AvailabilityService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<AvailabilityEntryDto>> ListAsync(Guid employeeId, CancellationToken ct = default) =>
        await _db.AvailabilityEntries.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.EffectiveFrom)
            .Select(a => new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent))
            .ToListAsync(ct);

    public async Task<AvailabilityEntryDto> AddAsync(Guid employeeId, SaveAvailabilityEntryDto dto, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            throw new NotFoundException(nameof(Employee), employeeId);
        if (await _db.AvailabilityEntries.AnyAsync(a => a.EmployeeId == employeeId && a.EffectiveFrom == dto.EffectiveFrom, ct))
            throw new ConflictException($"An availability entry already exists for {dto.EffectiveFrom}.");

        var a = new AvailabilityEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            EffectiveFrom = dto.EffectiveFrom,
            CapacityPercent = dto.CapacityPercent,
        };
        _db.AvailabilityEntries.Add(a);
        await _db.SaveChangesAsync(ct);
        return new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent);
    }

    public async Task<AvailabilityEntryDto> UpdateAsync(Guid entryId, SaveAvailabilityEntryDto dto, CancellationToken ct = default)
    {
        var a = await _db.AvailabilityEntries.FirstOrDefaultAsync(x => x.Id == entryId, ct)
            ?? throw new NotFoundException(nameof(AvailabilityEntry), entryId);
        if (a.EffectiveFrom != dto.EffectiveFrom &&
            await _db.AvailabilityEntries.AnyAsync(x => x.EmployeeId == a.EmployeeId && x.EffectiveFrom == dto.EffectiveFrom, ct))
            throw new ConflictException($"An availability entry already exists for {dto.EffectiveFrom}.");

        a.EffectiveFrom = dto.EffectiveFrom;
        a.CapacityPercent = dto.CapacityPercent;
        await _db.SaveChangesAsync(ct);
        return new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent);
    }

    public async Task DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        var a = await _db.AvailabilityEntries.FirstOrDefaultAsync(x => x.Id == entryId, ct)
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
