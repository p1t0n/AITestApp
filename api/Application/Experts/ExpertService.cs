using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

/// <summary>An agent-staged draft: the created expert plus a cheap duplicate warning when an
/// existing expert already carries the same (normalized) full name.</summary>
public record IngestionDraftDto(ExpertDetailDto Expert, string? DuplicateWarning);

public interface IExpertService
{
    /// <summary>Active experts only by default; drafts opt in (review surfaces).</summary>
    Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default);
    Task<ExpertDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExpertDetailDto> CreateAsync(SaveExpertDto dto, CancellationToken ct = default);
    /// <summary>Creates a Draft expert (resume ingestion): invisible to roster/search/staffing
    /// until promoted. Returns a duplicate warning when an Active same-name expert exists.</summary>
    Task<IngestionDraftDto> CreateDraftAsync(SaveExpertDto dto, CancellationToken ct = default);
    /// <summary>Flips a Draft to Active — the human publication gate. Requires a valid email.</summary>
    Task<ExpertDetailDto> PromoteAsync(Guid id, CancellationToken ct = default);
    Task<ExpertDetailDto> UpdateAsync(Guid id, SaveExpertDto dto, CancellationToken ct = default);
    /// <summary>Partial update: only the fields present in <paramref name="dto"/> change.</summary>
    Task<ExpertDetailDto> PatchAsync(Guid id, UpdateExpertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ExpertService : IExpertService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveExpertDto> _validator;
    private readonly IValidator<UpdateExpertDto> _patchValidator;
    public ExpertService(IAppDbContext db, IValidator<SaveExpertDto> validator, IValidator<UpdateExpertDto> patchValidator)
    {
        _db = db;
        _validator = validator;
        _patchValidator = patchValidator;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default)
    {
        var experts = await _db.Experts
            .AsNoTracking()
            .Where(e => includeDrafts || e.Status == ExpertStatus.Active)
            .Include(e => e.AvailabilityEntries)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return experts.Select(e => e.ToSummary(Today)).ToList();
    }

    public async Task<ExpertDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await LoadFullAsync(id, track: false, ct);
        if (e is null) throw new NotFoundException(nameof(Expert), id);
        return e.ToDetail(Today);
    }

    public async Task<ExpertDetailDto> CreateAsync(SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Expert { Id = Guid.NewGuid() };
        Apply(e, dto);
        _db.Experts.Add(e);
        await SaveGuardingEmailAsync(e.Email, "Use the existing expert, or give this one a different address.", ct);
        return await GetAsync(e.Id, ct);
    }

    public async Task<IngestionDraftDto> CreateDraftAsync(SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Expert { Id = Guid.NewGuid(), Status = ExpertStatus.Draft };
        Apply(e, dto);
        _db.Experts.Add(e);
        await _db.SaveChangesAsync(ct);

        // Cheap duplicate signal, decided at create time from data (never model text): a
        // case-insensitive full-name match against anyone else on the roster.
        var first = dto.FirstName.Trim().ToLower();
        var last = dto.LastName.Trim().ToLower();
        var duplicate = await _db.Experts
            .AsNoTracking()
            .Where(x => x.Id != e.Id
                        && x.FirstName.ToLower() == first
                        && x.LastName.ToLower() == last)
            .Select(x => new { x.Title, x.Status })
            .FirstOrDefaultAsync(ct);

        var warning = duplicate is null
            ? null
            : $"An expert named {dto.FirstName.Trim()} {dto.LastName.Trim()} already exists ({duplicate.Title}, {duplicate.Status}). Review before promoting.";

        return new IngestionDraftDto(await GetAsync(e.Id, ct), warning);
    }

    public async Task<ExpertDetailDto> PromoteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Experts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);

        if (e.Status == ExpertStatus.Active)
        {
            return await GetAsync(id, ct); // idempotent: promoting an Active expert is a no-op
        }

        // The publication gate demands the one field drafts may honestly lack.
        if (string.IsNullOrWhiteSpace(e.Email) || !new SaveExpertValidator().Validate(
                new SaveExpertDto(e.FirstName, e.LastName, e.Title, e.Email, e.Phone, e.Location, e.Summary, e.PhotoUrl)).IsValid)
        {
            throw new ValidationException("A valid email is required to promote a draft expert.");
        }

        e.Status = ExpertStatus.Active;
        // The partial unique index only binds Active rows, so a draft's clash surfaces exactly here.
        await SaveGuardingEmailAsync(e.Email, "Resolve the duplicate before promoting.", ct);

        return await GetAsync(id, ct);
    }

    public async Task<ExpertDetailDto> UpdateAsync(Guid id, SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = await _db.Experts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        Apply(e, dto);
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this expert.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<ExpertDetailDto> PatchAsync(Guid id, UpdateExpertDto dto, CancellationToken ct = default)
    {
        await _patchValidator.ValidateAndThrowAsync(dto, ct);
        var e = await _db.Experts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        ApplyPatch(e, dto);
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this expert.", ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Experts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        _db.Experts.Remove(e);
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
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Experts_Email") == true)
        {
            throw new ConflictException($"An active expert already uses the email '{email}'. {remedy}");
        }
    }

    private async Task<Expert?> LoadFullAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = _db.Experts.AsQueryable();
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

    private static void Apply(Expert e, SaveExpertDto dto)
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
    private static void ApplyPatch(Expert e, UpdateExpertDto dto)
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
