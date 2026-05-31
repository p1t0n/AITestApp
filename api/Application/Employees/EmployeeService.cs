using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Common;
using EmployeeManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Application.Employees;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<EmployeeDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDetailDto> CreateAsync(SaveEmployeeDto dto, CancellationToken ct = default);
    Task<EmployeeDetailDto> UpdateAsync(Guid id, SaveEmployeeDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class EmployeeService : IEmployeeService
{
    private readonly IAppDbContext _db;
    public EmployeeService(IAppDbContext db) => _db = db;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<EmployeeSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Include(e => e.AvailabilityEntries)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return employees.Select(e => e.ToSummary(Today)).ToList();
    }

    public async Task<EmployeeDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await LoadFullAsync(id, track: false, ct);
        if (e is null) throw new NotFoundException(nameof(Employee), id);
        return e.ToDetail(Today);
    }

    public async Task<EmployeeDetailDto> CreateAsync(SaveEmployeeDto dto, CancellationToken ct = default)
    {
        var e = new Employee { Id = Guid.NewGuid() };
        Apply(e, dto);
        _db.Employees.Add(e);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(e.Id, ct);
    }

    public async Task<EmployeeDetailDto> UpdateAsync(Guid id, SaveEmployeeDto dto, CancellationToken ct = default)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);
        Apply(e, dto);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Employee?> LoadFullAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = _db.Employees.AsQueryable();
        if (!track) query = query.AsNoTracking();
        return await query
            .Include(e => e.SpokenLanguages)
            .Include(e => e.AvailabilityEntries)
            .Include(e => e.Skills).ThenInclude(s => s.Skill).ThenInclude(s => s.Category)
            .Include(e => e.Qualifications)
            .Include(e => e.Experiences).ThenInclude(x => x.Achievements)
            .Include(e => e.Experiences).ThenInclude(x => x.Skills).ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    private static void Apply(Employee e, SaveEmployeeDto dto)
    {
        e.FirstName = dto.FirstName.Trim();
        e.LastName = dto.LastName.Trim();
        e.Title = dto.Title.Trim();
        e.Email = dto.Email.Trim();
        e.Phone = dto.Phone;
        e.Location = dto.Location;
        e.Summary = dto.Summary;
        e.PhotoUrl = dto.PhotoUrl;
    }
}
