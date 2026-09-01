using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Search;

/// <summary>Resolves retrieval filters to a deterministic eligible-expert id set — the shared
/// semantics of semantic search's SQL prefilter, exposed for callers (Roster Scan) that filter
/// without ranking. Null means "no filters set" (everyone active is eligible).</summary>
public interface IExpertFilterService
{
    Task<HashSet<Guid>?> ResolveEligibleAsync(SemanticSearchFilters? filters, CancellationToken ct = default);
}

/// <summary>Mirrors <c>SemanticSearchService.ResolveEligibleExpertsAsync</c> exactly (location
/// case-insensitive equality, every requested skill — each meeting MinYears when set — and the
/// availability step-function: latest entry on/before the date with capacity &gt; 0).</summary>
public sealed class ExpertFilterService(IAppDbContext db) : IExpertFilterService
{
    public async Task<HashSet<Guid>?> ResolveEligibleAsync(
        SemanticSearchFilters? filters, CancellationToken ct = default)
    {
        if (filters is null)
        {
            return null;
        }

        var hasSkills = filters.SkillIds is { Count: > 0 };
        var hasFilter = filters.AvailableOn is not null
            || !string.IsNullOrWhiteSpace(filters.Location)
            || hasSkills
            || filters.MinYears is not null;
        if (!hasFilter)
        {
            return null;
        }

        IQueryable<Expert> q = db.Experts.Where(e => e.Status == ExpertStatus.Active);

        if (!string.IsNullOrWhiteSpace(filters.Location))
        {
            var loc = filters.Location.Trim().ToLower();
            q = q.Where(e => e.Location != null && e.Location.ToLower() == loc);
        }

        if (hasSkills)
        {
            foreach (var skillId in filters.SkillIds!)
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

        var ids = await q.Select(e => e.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }
}
