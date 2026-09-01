using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Search;

/// <summary>Resolves retrieval filters to a deterministic eligible-expert id set — the shared
/// semantics of semantic search's SQL prefilter, exposed for callers (Roster Scan) that filter
/// without ranking. Null means "no filters set" (everyone on the bench is eligible).</summary>
public interface IExpertFilterService
{
    Task<HashSet<Guid>?> ResolveEligibleAsync(SemanticSearchFilters? filters, CancellationToken ct = default);

    /// <summary>
    /// How many rows the Roster Scan would actually enumerate under these filters — the same
    /// predicates, intersected with the Art. 22(2) route (P1T-185). Separate from
    /// <see cref="ResolveEligibleAsync"/> because search and the scan share the filters and not the
    /// exception: search ranks anybody on the bench, the scan may only profile a row that has a
    /// route. Without it the submit-time estimate counts people the sweep then skips, and a
    /// progress bar that starts by overstating its total is a progress bar that lies.
    /// </summary>
    Task<int> CountScannableAsync(SemanticSearchFilters? filters, CancellationToken ct = default);
}

/// <summary>Mirrors <c>SemanticSearchService.ResolveEligibleExpertsAsync</c> exactly (location
/// case-insensitive equality, every requested skill — each meeting MinYears when set — and the
/// availability step-function: latest entry on/before the date with capacity &gt; 0).</summary>
public sealed class ExpertFilterService(IAppDbContext db) : IExpertFilterService
{
    public async Task<HashSet<Guid>?> ResolveEligibleAsync(
        SemanticSearchFilters? filters, CancellationToken ct = default)
    {
        if (!HasAnyFilter(filters))
        {
            return null;
        }

        // Availability-shaped, so it filters for visibility unconditionally rather than asking an
        // audience: an Expert who paused themselves is eligible for nothing (P1T-185).
        var ids = await Apply(db.Experts.OnTheBench(), filters).Select(e => e.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public Task<int> CountScannableAsync(
        SemanticSearchFilters? filters, CancellationToken ct = default) =>
        Apply(db.Experts.Scannable(), filters).CountAsync(ct);

    private static bool HasAnyFilter(SemanticSearchFilters? filters) =>
        filters is not null
        && (filters.AvailableOn is not null
            || !string.IsNullOrWhiteSpace(filters.Location)
            || filters.SkillIds is { Count: > 0 }
            || filters.MinYears is not null);

    /// <summary>
    /// The filters themselves, over whatever population the caller already narrowed to. Which
    /// population that is stays the caller's choice — <c>OnTheBench()</c> for retrieval,
    /// <c>Scannable()</c> for the scan — while the filter predicates are written once, here, so the
    /// two can never drift apart.
    /// </summary>
    private static IQueryable<Expert> Apply(IQueryable<Expert> q, SemanticSearchFilters? filters)
    {
        if (filters is null)
        {
            return q;
        }

        if (!string.IsNullOrWhiteSpace(filters.Location))
        {
            var loc = filters.Location.Trim().ToLower();
            q = q.Where(e => e.Location != null && e.Location.ToLower() == loc);
        }

        if (filters.SkillIds is { Count: > 0 })
        {
            foreach (var skillId in filters.SkillIds)
            {
                var min = filters.MinYears;
                q = q.Where(e => e.Skills.Any(s =>
                    s.SkillId == skillId && (min == null || s.YearsExperience >= min)));
            }
        }
        else if (filters.MinYears is { } minYears)
        {
            q = q.Where(e => e.Skills.Any(s => s.YearsExperience >= minYears));
        }

        if (filters.AvailableOn is { } onDate)
        {
            q = q.Where(e => e.AvailabilityEntries
                .Where(a => a.EffectiveFrom <= onDate)
                .OrderByDescending(a => a.EffectiveFrom)
                .Select(a => a.CapacityPercent)
                .FirstOrDefault() > 0);
        }

        return q;
    }
}
