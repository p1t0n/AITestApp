using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Employees;

/// <summary>An agent-staged draft: the created employee plus a cheap duplicate warning when an
/// existing employee already carries the same (normalized) full name.</summary>
public record IngestionDraftDto(EmployeeDetailDto Employee, string? DuplicateWarning);

public interface IEmployeeService
{
    /// <summary>Active employees only by default; drafts opt in (review surfaces).</summary>
    Task<IReadOnlyList<EmployeeSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default);
    Task<EmployeeDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDetailDto> CreateAsync(SaveEmployeeDto dto, CancellationToken ct = default);
    /// <summary>Creates a Draft employee (resume ingestion): invisible to roster/search/staffing
    /// until promoted. Returns a duplicate warning when an Active same-name employee exists.</summary>
    Task<IngestionDraftDto> CreateDraftAsync(SaveEmployeeDto dto, CancellationToken ct = default);
    /// <summary>Flips a Draft to Active — the human publication gate. Requires a valid email.</summary>
    Task<EmployeeDetailDto> PromoteAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDetailDto> UpdateAsync(Guid id, SaveEmployeeDto dto, CancellationToken ct = default);
    /// <summary>Partial update: only the fields present in <paramref name="dto"/> change.</summary>
    Task<EmployeeDetailDto> PatchAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class EmployeeService : IEmployeeService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveEmployeeDto> _validator;
    private readonly IValidator<UpdateEmployeeDto> _patchValidator;
    public EmployeeService(IAppDbContext db, IValidator<SaveEmployeeDto> validator, IValidator<UpdateEmployeeDto> patchValidator)
    {
        _db = db;
        _validator = validator;
        _patchValidator = patchValidator;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<EmployeeSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default)
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => includeDrafts || e.Status == EmployeeStatus.Active)
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
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Employee { Id = Guid.NewGuid() };
        Apply(e, dto);
        _db.Employees.Add(e);
        await SaveGuardingEmailAsync(e.Email, "Use the existing employee, or give this one a different address.", ct);
        return await GetAsync(e.Id, ct);
    }

    public async Task<IngestionDraftDto> CreateDraftAsync(SaveEmployeeDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Employee { Id = Guid.NewGuid(), Status = EmployeeStatus.Draft };
        Apply(e, dto);
        _db.Employees.Add(e);
        await _db.SaveChangesAsync(ct);

        // Cheap duplicate signal, decided at create time from data (never model text): a
        // case-insensitive full-name match against anyone else on the roster.
        var first = dto.FirstName.Trim().ToLower();
        var last = dto.LastName.Trim().ToLower();
        var duplicate = await _db.Employees
            .AsNoTracking()
            .Where(x => x.Id != e.Id
                        && x.FirstName.ToLower() == first
                        && x.LastName.ToLower() == last)
            .Select(x => new { x.Title, x.Status })
            .FirstOrDefaultAsync(ct);

        var warning = duplicate is null
            ? null
            : $"An employee named {dto.FirstName.Trim()} {dto.LastName.Trim()} already exists ({duplicate.Title}, {duplicate.Status}). Review before promoting.";

        return new IngestionDraftDto(await GetAsync(e.Id, ct), warning);
    }

    public async Task<EmployeeDetailDto> PromoteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);

        if (e.Status == EmployeeStatus.Active)
        {
            return await GetAsync(id, ct); // idempotent: promoting an Active employee is a no-op
        }

        // The publication gate demands the one field drafts may honestly lack.
        if (string.IsNullOrWhiteSpace(e.Email) || !new SaveEmployeeValidator().Validate(
                new SaveEmployeeDto(e.FirstName, e.LastName, e.Title, e.Email, e.Phone, e.Location, e.Summary, e.PhotoUrl)).IsValid)
        {
            throw new ValidationException("A valid email is required to promote a draft employee.");
        }

        e.Status = EmployeeStatus.Active;
        // The partial unique index only binds Active rows, so a draft's clash surfaces exactly here.
        await SaveGuardingEmailAsync(e.Email, "Resolve the duplicate before promoting.", ct);

        return await GetAsync(id, ct);
    }

    public async Task<EmployeeDetailDto> UpdateAsync(Guid id, SaveEmployeeDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);
        Apply(e, dto);
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this employee.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<EmployeeDetailDto> PatchAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default)
    {
        await _patchValidator.ValidateAndThrowAsync(dto, ct);
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);
        ApplyPatch(e, dto);
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this employee.", ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Saves, translating the roster's one database-level uniqueness rule into a Conflict. Email
    /// uniqueness lives in a partial unique index over Active rows — a rule EF cannot pre-check
    /// without a race — so the clash can only ever be caught here, on the way out. Left unhandled it
    /// reaches the caller as a 500 for what is an ordinary, correctable mistake (P1T-140).
    /// </summary>
    private async Task SaveGuardingEmailAsync(string email, string remedy, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Employees_Email") == true)
        {
            throw new ConflictException($"An active employee already uses the email '{email}'. {remedy}");
        }
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

    /// <summary>Only overwrites fields present (non-null) in <paramref name="dto"/>; an omitted
    /// field keeps its current value.</summary>
    private static void ApplyPatch(Employee e, UpdateEmployeeDto dto)
    {
        if (dto.FirstName is not null) e.FirstName = dto.FirstName.Trim();
        if (dto.LastName is not null) e.LastName = dto.LastName.Trim();
        if (dto.Title is not null) e.Title = dto.Title.Trim();
        if (dto.Email is not null) e.Email = dto.Email.Trim();
        if (dto.Phone is not null) e.Phone = dto.Phone;
        if (dto.Location is not null) e.Location = dto.Location;
        if (dto.Summary is not null) e.Summary = dto.Summary;
        if (dto.PhotoUrl is not null) e.PhotoUrl = dto.PhotoUrl;
    }
}
