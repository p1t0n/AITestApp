using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
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
    private readonly IOwnershipScopeProvider _scope;
    private readonly TimeProvider _clock;
    public ExpertService(
        IAppDbContext db,
        IValidator<SaveExpertDto> validator,
        IValidator<UpdateExpertDto> patchValidator,
        IOwnershipScopeProvider scope,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _patchValidator = patchValidator;
        _scope = scope;
        _clock = clock;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default)
    {
        // Scoped too, though the roster endpoint itself is Service Manager only: this is the one
        // call that would hand over the whole product, so it does not rely on a single [Authorize]
        // somewhere above it being right.
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var experts = await _db.Experts
            .AsNoTracking()
            .Where(e => (includeDrafts || e.Status == ExpertStatus.Active)
                        && (unrestricted || e.Id == owned))
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

    /// <summary>
    /// Reads back a row this call has just written, ignoring the caller's scope. Only ever called
    /// with an id the same method already resolved *through* the scope, so the check has happened —
    /// re-applying it here would 404 the one legitimate case where it must not: a Service Manager
    /// creating a row, and an Expert saving their own.
    /// </summary>
    private async Task<ExpertDetailDto> ReadBackAsync(Guid id, CancellationToken ct)
    {
        var e = await LoadFullAsync(id, track: false, ct, OwnershipScope.Unrestricted);
        if (e is null) throw new NotFoundException(nameof(Expert), id);
        return e.ToDetail(Today);
    }

    public async Task<ExpertDetailDto> CreateAsync(SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Expert { Id = Guid.NewGuid() };
        Apply(e, dto);
        RecordCreation(e, "Added to the bench by a Service Manager.");
        _db.Experts.Add(e);
        await SaveGuardingEmailAsync(e.Email, "Use the existing expert, or give this one a different address.", ct);
        return await ReadBackAsync(e.Id, ct);
    }

    public async Task<IngestionDraftDto> CreateDraftAsync(SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Expert { Id = Guid.NewGuid(), Status = ExpertStatus.Draft };
        Apply(e, dto);
        RecordCreation(e, "Staged from a resume by an ingestion agent, on behalf of a Service Manager.");
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

        return new IngestionDraftDto(await ReadBackAsync(e.Id, ct), warning);
    }

    public async Task<ExpertDetailDto> PromoteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await LoadScopedAsync(id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);

        if (e.Status == ExpertStatus.Active)
        {
            return await ReadBackAsync(id, ct); // idempotent: promoting an Active expert is a no-op
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

        return await ReadBackAsync(id, ct);
    }

    public async Task<ExpertDetailDto> UpdateAsync(Guid id, SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = await LoadScopedAsync(id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        var frozenEmail = await FrozenEmailAsync(e, dto.Email, ct);
        Apply(e, dto);
        if (frozenEmail is not null) e.Email = frozenEmail;
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this expert.", ct);
        return await ReadBackAsync(id, ct);
    }

    public async Task<ExpertDetailDto> PatchAsync(Guid id, UpdateExpertDto dto, CancellationToken ct = default)
    {
        await _patchValidator.ValidateAndThrowAsync(dto, ct);
        var e = await LoadScopedAsync(id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        var frozenEmail = await FrozenEmailAsync(e, dto.Email, ct);
        ApplyPatch(e, dto);
        if (frozenEmail is not null) e.Email = frozenEmail;
        await SaveGuardingEmailAsync(e.Email, "Use a different address for this expert.", ct);
        return await ReadBackAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await LoadScopedAsync(id, ct)
            ?? throw new NotFoundException(nameof(Expert), id);
        _db.Experts.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Attaches the row's first <see cref="ProcessingRecord"/> (P1T-183) — in the same graph, so it
    /// is written in the same transaction as the Expert. A roster row that exists for even one
    /// commit without a recorded lawful basis is a compliance defect, so this is not a follow-up
    /// call that could fail on its own.
    ///
    /// <para>The origin is <see cref="ProcessingOrigin.StaffCreated"/> on both creation paths
    /// because both of them <em>are</em> staff creating a row — the API's POST is Service-Manager
    /// only, and an ingestion agent stages drafts for a Service Manager to promote. This is not a
    /// default standing in for an unknown: registering does not create a roster row at all, so the
    /// self-registered origin is reached by an approved claim appending a record (P1T-184), never by
    /// a create. The basis itself is not chosen here — <see cref="ProcessingRecord.BasisFor"/> and
    /// the table's CHECK constraint decide it from the origin.</para>
    /// </summary>
    private void RecordCreation(Expert e, string reason) =>
        e.ProcessingRecords.Add(ProcessingRecord.For(
            e.Id, sequence: 1, ProcessingOrigin.StaffCreated,
            noticeVersion: null, reason, _clock.GetUtcNow()));

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

    /// <summary>The tracked row, if this caller may reach it. Null covers both "no such row" and
    /// "not yours", which is the whole point — the caller cannot tell the two apart.</summary>
    private async Task<Expert?> LoadScopedAsync(Guid id, CancellationToken ct)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        return await _db.Experts
            .FirstOrDefaultAsync(x => x.Id == id && (unrestricted || x.Id == owned), ct);
    }

    private async Task<Expert?> LoadFullAsync(
        Guid id, bool track, CancellationToken ct, OwnershipScope? scope = null)
    {
        var (unrestricted, owned) = scope ?? await _scope.CurrentAsync(ct);
        var query = _db.Experts.AsQueryable();
        if (!track) query = query.AsNoTracking();
        return await query
            .Include(e => e.SpokenLanguages)
            .Include(e => e.AvailabilityEntries)
            .Include(e => e.Skills).ThenInclude(s => s.Skill).ThenInclude(s => s.Category)
            .Include(e => e.Qualifications)
            .Include(e => e.Experiences).ThenInclude(x => x.Achievements)
            .Include(e => e.Experiences).ThenInclude(x => x.Skills).ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(e => e.Id == id && (unrestricted || e.Id == owned), ct);
    }

    /// <summary>
    /// Email is set at registration and is Service-Manager-only thereafter (P1T-184). A security
    /// rule, not a UX limitation: the address is login identifier, claim key and CV contact at the
    /// same time, with no verification behind any of them — so an owner who could edit it could
    /// point their row at a bench member's address and re-trigger claim matching, reaching the
    /// takeover the pending-claim design exists to prevent through the my-account door instead.
    ///
    /// <para>Returns the address to pin the row back to when the caller is not staff, or null when
    /// they are and it may move. A real change is <em>refused</em> rather than silently ignored —
    /// somebody who tried needs to be told it did not happen and who can do it — while a
    /// case-only difference is neither a change nor an error, and the stored value simply stands.</para>
    /// </summary>
    private async Task<string?> FrozenEmailAsync(Expert e, string? submitted, CancellationToken ct)
    {
        var (unrestricted, _) = await _scope.CurrentAsync(ct);
        if (unrestricted)
        {
            return null;
        }

        if (submitted is not null
            && !string.Equals(submitted.Trim(), e.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(SaveExpertDto.Email),
                "Your email address is set when you register and can only be changed by a Service " +
                "Manager. It identifies your account and links you to this record.")]);
        }

        return e.Email;
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
