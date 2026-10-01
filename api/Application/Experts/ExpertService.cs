using System.Linq.Expressions;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Visibility;
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
    /// <summary>The same bench listing, narrowed and counted (EXP-94): optional case-insensitive
    /// substring filters on location and status, plus the total of the whole match. The filters are
    /// here rather than in a shell so REST and MCP narrow identically.</summary>
    Task<ExpertListResult> ListAsync(ExpertListQuery query, CancellationToken ct = default);
    /// <summary>One page of the whole Roster — Draft, Active and Paused — searched, sorted and
    /// counted in SQL (EXP-45). The staff roster's query; <see cref="ListAsync"/> stays the bench's.</summary>
    Task<RosterPage> SearchAsync(RosterQuery query, CancellationToken ct = default);
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
    private readonly IValidator<RosterQuery> _rosterValidator;
    private readonly IOwnershipScopeProvider _scope;
    private readonly IRosterAudienceProvider _audience;
    private readonly TimeProvider _clock;
    public ExpertService(
        IAppDbContext db,
        IValidator<SaveExpertDto> validator,
        IValidator<UpdateExpertDto> patchValidator,
        IValidator<RosterQuery> rosterValidator,
        IOwnershipScopeProvider scope,
        IRosterAudienceProvider audience,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _patchValidator = patchValidator;
        _rosterValidator = rosterValidator;
        _scope = scope;
        _audience = audience;
        _clock = clock;
    }

    /// <summary>
    /// The date an Expert's <c>currentCapacityPercent</c> is resolved against — from the injected
    /// clock, like every other time this service reads. It used to be <c>DateTime.UtcNow</c>, which
    /// made the roster's shape a function of the machine it ran on: the Cost Floor over the seeded
    /// demo roster measures a payload whose capacity values change width as the calendar crosses a
    /// seeded availability date, so <c>main</c> went red overnight with no code change (P1T-199).
    /// </summary>
    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(bool includeDrafts = false, CancellationToken ct = default)
        => (await ListAsync(new ExpertListQuery(IncludeDrafts: includeDrafts), ct)).Items;

    public async Task<ExpertListResult> ListAsync(ExpertListQuery query, CancellationToken ct = default)
    {
        // Scoped too, though the roster endpoint itself is Administrator only: this is the one
        // call that would hand over the whole product, so it does not rely on a single [Authorize]
        // somewhere above it being right.
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        // Two seams, two questions: the ownership scope says who is asking (P1T-182), the audience
        // says what the row permits (P1T-185). An agent is unrestricted on the first and still
        // never sees a paused Expert here. The filters are applied AFTER both, so a count is a
        // count of what the caller may see — a hidden Expert in Warsaw is not one of the 31.
        var matches = WithListFilters(
            _db.Experts
                .AsNoTracking()
                .ForAudience(_audience.Current, query.IncludeDrafts)
                .Where(e => unrestricted || e.Id == owned),
            query);

        var total = await matches.CountAsync(ct);
        var experts = await matches
            .Include(e => e.AvailabilityEntries)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return new ExpertListResult(total, experts.Select(e => e.ToSummary(Today)).ToList());
    }

    /// <summary>
    /// The bench listing's optional narrowing (EXP-94). Both filters are case-insensitive
    /// substrings, because the caller is a person (or a model) typing "warsaw", not picking from a
    /// facet list — the roster screen's exact-match <see cref="RosterQuery.Locations"/> is the
    /// other question and keeps its own code.
    /// </summary>
    private static IQueryable<Expert> WithListFilters(IQueryable<Expert> experts, ExpertListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            // ToLower rather than string.Contains(..., StringComparison), for the same reason the
            // skill catalog's nameContains uses it (P1T-145): EF Core translates the former to SQL
            // on Postgres and on the in-memory provider the unit tests run on.
            var needle = query.Location.Trim().ToLower();
            experts = experts.Where(e => e.Location != null && e.Location.ToLower().Contains(needle));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // Resolved to enum values here rather than compared in SQL: the status is stored as a
            // number, so there is no column to run a substring over. A needle that names no status
            // leaves this empty, and an empty set matches nobody — a misspelled filter answers
            // zero rather than silently answering "everyone".
            var needle = query.Status.Trim().ToLowerInvariant();
            var named = Enum.GetValues<ExpertStatus>()
                .Where(s => s.ToString().ToLowerInvariant().Contains(needle))
                .ToArray();
            experts = experts.Where(e => named.Contains(e.Status));
        }

        return experts;
    }

    public async Task<RosterPage> SearchAsync(RosterQuery query, CancellationToken ct = default)
    {
        await _rosterValidator.ValidateAndThrowAsync(query, ct);

        // Same two seams as ListAsync, and the same reason: the scope says who is asking, the
        // audience says what the row permits. Drafts are always in — this is the Roster, and a
        // Draft nothing lists is a Draft nobody can promote (EXP-49).
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var today = Today;

        var visible = _db.Experts
            .AsNoTracking()
            .ForAudience(_audience.Current, includeDrafts: true)
            .Where(e => unrestricted || e.Id == owned);

        var searched = WithSearch(visible, query.Q);

        var statuses = RosterStatuses.Selected(query.Statuses);
        var locations = SelectedLocations(query.Locations);
        var band = RosterBand.Normalize(query.Band);

        // The facet rule, spelled out by what each line leaves out (EXP-47): a group is counted
        // against every *other* active filter and never against its own, so an unchecked box says
        // what checking it would add rather than the 0 its own filter would force it to.
        var facets = new RosterFacets(
            Status: await CountByStatusAsync(WithBand(WithLocations(searched, locations), band, today), ct),
            Band: await CountByBandAsync(WithLocations(WithStatuses(searched, statuses), locations), today, ct),
            Location: await CountByLocationAsync(
                WithBand(WithStatuses(searched, statuses), band, today), searched, locations, ct));

        var matches = WithBand(WithLocations(WithStatuses(searched, statuses), locations), band, today);

        // Counted before the slice is taken, and by the database: the heading says "N experts" and
        // the footer "Showing from–to of total", and both are wrong the moment N is a page length.
        var total = await matches.CountAsync(ct);

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? RosterPaging.DefaultPageSize;

        var experts = await InOrder(matches, query, today)
            .Include(e => e.AvailabilityEntries)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new RosterPage(experts.Select(e => e.ToSummary(today)).ToList(), total, facets);
    }

    private static IQueryable<Expert> WithSearch(IQueryable<Expert> experts, string? q)
    {
        var needle = q?.Trim().ToLower();
        if (string.IsNullOrEmpty(needle)) return experts;

        // ToLower rather than string.Contains(..., StringComparison): EF Core translates the
        // former to SQL on both Postgres and the in-memory provider the unit tests run on. The
        // name is matched as one string so "ada love" finds Ada Lovelace, which neither half
        // would on its own.
        return experts.Where(e =>
            (e.FirstName + " " + e.LastName).ToLower().Contains(needle)
            || e.Email.ToLower().Contains(needle)
            || e.Title.ToLower().Contains(needle));
    }

    /// <summary>Nothing checked is not "match nothing" — it is the group left alone.</summary>
    private static IQueryable<Expert> WithStatuses(
        IQueryable<Expert> experts, IReadOnlyList<ExpertStatus> statuses) =>
        statuses.Count == 0 ? experts : experts.Where(e => statuses.Contains(e.Status));

    private static IQueryable<Expert> WithLocations(
        IQueryable<Expert> experts, IReadOnlyList<string> locations) =>
        locations.Count == 0
            ? experts
            // Somebody with no location is in no location's bucket, so choosing any place at all
            // excludes them. That is the same rule the sidebar draws: a blank is not a place.
            : experts.Where(e => e.Location != null && locations.Contains(e.Location));

    private static IQueryable<Expert> WithBand(
        IQueryable<Expert> experts, string? band, DateOnly today)
    {
        if (band is null) return experts;
        var (min, max) = RosterBand.Range(band);
        return experts.Where(CapacityCalculator.CapacityBetween(today, min, max));
    }

    /// <summary>The locations a query actually filters on: trimmed, de-duplicated, blanks dropped —
    /// a blank would be a filter for people who have no location, which the sidebar cannot ask for
    /// and the checkbox list does not offer.</summary>
    private static IReadOnlyList<string> SelectedLocations(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return [];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every status, at zero if need be: a checkbox that vanishes when its count reaches
    /// zero cannot be unchecked back into view.</summary>
    private static async Task<IReadOnlyList<RosterFacetCount>> CountByStatusAsync(
        IQueryable<Expert> experts, CancellationToken ct)
    {
        var counted = await experts
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return RosterStatuses.Keys
            .Select(name => new RosterFacetCount(
                name,
                counted.FirstOrDefault(c => c.Status.ToString() == name)?.Count ?? 0))
            .ToList();
    }

    /// <summary>
    /// One <c>COUNT</c> per band rather than a <c>GROUP BY</c> over a CASE: the band is a
    /// correlated subquery compared against two bounds, and three cheap counted predicates
    /// translate everywhere the roster runs — including the in-memory provider the rules are
    /// settled over — where a grouping key built from that subquery does not.
    /// </summary>
    private static async Task<IReadOnlyList<RosterFacetCount>> CountByBandAsync(
        IQueryable<Expert> experts, DateOnly today, CancellationToken ct)
    {
        var counts = new List<RosterFacetCount>(RosterBand.Keys.Count);
        foreach (var band in RosterBand.Keys)
        {
            var (min, max) = RosterBand.Range(band);
            counts.Add(new RosterFacetCount(
                band,
                await experts.CountAsync(CapacityCalculator.CapacityBetween(today, min, max), ct)));
        }
        return counts;
    }

    /// <summary>
    /// The location rows, busiest first with the name breaking ties — a scrollable list needs one
    /// fixed order, and "where are most of these people" is the question it is scanned for.
    ///
    /// <para>Two queries, because which rows exist and how many there are answer different
    /// questions. The numbers come from <paramref name="experts"/> — the match under every group
    /// but this one — so a place the status or band filter has counted down to zero keeps its row
    /// and is greyed out rather than vanishing; dropping it would leave widening another group as
    /// the only way to discover the place exists.</para>
    ///
    /// <para>The rows come from <paramref name="searched"/>, the match under the search alone.
    /// The search is the sidebar's own coarse cut and the list should follow it: after typing a
    /// name, offering forty cities at zero is noise, not information. <paramref name="selected"/>
    /// is unioned back in regardless, so a location somebody has ticked never disappears out from
    /// under them and leaves a filter they cannot see or undo.</para>
    /// </summary>
    private static async Task<IReadOnlyList<RosterFacetCount>> CountByLocationAsync(
        IQueryable<Expert> experts,
        IQueryable<Expert> searched,
        IReadOnlyList<string> selected,
        CancellationToken ct)
    {
        var counted = await Placed(experts)
            .GroupBy(e => e.Location!)
            .Select(g => new { Location = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var offered = await Placed(searched)
            .Select(e => e.Location!)
            .Distinct()
            .ToListAsync(ct);

        return offered
            .Union(selected, StringComparer.Ordinal)
            .Select(l => new RosterFacetCount(
                l, counted.FirstOrDefault(c => c.Location == l)?.Count ?? 0))
            .OrderByDescending(f => f.Count)
            .ThenBy(f => f.Value, StringComparer.Ordinal)
            .ToList();
    }

    private static IQueryable<Expert> Placed(IQueryable<Expert> experts) =>
        experts.Where(e => e.Location != null && e.Location != "");

    /// <summary>
    /// The roster's order, as SQL. Every branch ends in the same total order, which is the part
    /// that matters for a paged list: two rows tied on the chosen key must still come back in one
    /// fixed sequence, or a page boundary falling between them repeats one row and skips another
    /// with nothing in the response to show it happened.
    /// </summary>
    private static IQueryable<Expert> InOrder(IQueryable<Expert> experts, RosterQuery query, DateOnly today)
    {
        var descending = RosterDirection.IsDescending(query.Dir);

        var chosen = RosterSort.Normalize(query.Sort) switch
        {
            RosterSort.Title => By(experts, e => e.Title, descending),
            // A missing location reads as the empty string rather than falling out of the order:
            // where NULLs land differs between providers, and the page has to be the same one
            // everywhere.
            RosterSort.Location => By(experts, e => e.Location ?? "", descending),
            RosterSort.Capacity => By(experts, CapacityCalculator.CapacityOn(today), descending),
            // Draft (1) before Active (2) ascending — the staff roster's unfinished work first,
            // which is what this list exists to surface.
            RosterSort.Status => By(experts, e => e.Status, descending),
            // null: the name order, which is also the tiebreak below, so it is applied once here
            // with the caller's direction rather than twice with two.
            _ => null,
        };

        var byName = chosen is null
            ? Then(By(experts, e => e.LastName, descending), e => e.FirstName, descending)
            : Then(Then(chosen, e => e.LastName, false), e => e.FirstName, false);

        // Id closes the order. Two people really can share a full name on this roster —
        // CreateDraftAsync exists to warn about exactly that — so name alone is not total.
        return byName.ThenBy(e => e.Id);
    }

    private static IOrderedQueryable<Expert> By<TKey>(
        IQueryable<Expert> experts, Expression<Func<Expert, TKey>> key, bool descending) =>
        descending ? experts.OrderByDescending(key) : experts.OrderBy(key);

    private static IOrderedQueryable<Expert> Then<TKey>(
        IOrderedQueryable<Expert> experts, Expression<Func<Expert, TKey>> key, bool descending) =>
        descending ? experts.ThenByDescending(key) : experts.ThenBy(key);

    public async Task<ExpertDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await LoadFullAsync(id, track: false, ct);
        if (e is null) throw new NotFoundException(nameof(Expert), id);
        return e.ToDetail(Today);
    }

    /// <summary>
    /// Reads back a row this call has just written, ignoring the caller's scope. Only ever called
    /// with an id the same method already resolved *through* the scope, so the check has happened —
    /// re-applying it here would 404 the one legitimate case where it must not: an Administrator
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
        RecordCreation(e, "Added to the bench by an Administrator.");
        _db.Experts.Add(e);
        await SaveGuardingEmailAsync(e.Email, "Use the existing expert, or give this one a different address.", ct);
        return await ReadBackAsync(e.Id, ct);
    }

    public async Task<IngestionDraftDto> CreateDraftAsync(SaveExpertDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var e = new Expert { Id = Guid.NewGuid(), Status = ExpertStatus.Draft };
        Apply(e, dto);
        RecordCreation(e, "Staged from a resume by an ingestion agent, on behalf of an Administrator.");
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
    /// only, and an ingestion agent stages drafts for an Administrator to promote. This is not a
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
            .ReachableBy(_audience.Current)
            .FirstOrDefaultAsync(x => x.Id == id && (unrestricted || x.Id == owned), ct);
    }

    private async Task<Expert?> LoadFullAsync(
        Guid id, bool track, CancellationToken ct, OwnershipScope? scope = null)
    {
        var (unrestricted, owned) = scope ?? await _scope.CurrentAsync(ct);
        var query = _db.Experts.ReachableBy(_audience.Current);
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
