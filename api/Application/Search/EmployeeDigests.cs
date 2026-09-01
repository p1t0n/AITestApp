using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Search;

/// <summary>One employee's compact career Digest (see CONTEXT.md): identity plus the narrative
/// text semantic search embeds — deterministic from the employee's own data.</summary>
public sealed record EmployeeDigest(Guid EmployeeId, string Name, string Title, string Digest);

/// <summary>One page of digests, with the roster total so a caller can size a full sweep.</summary>
public sealed record EmployeeDigestPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<EmployeeDigest> Items);

/// <summary>Paged career digests for bulk scoring (P1T-121). Serves the Roster Scan pipeline
/// through the <c>roster_digest_list</c> MCP tool — one call per scoring chunk instead of a
/// per-employee cv_get fan-out.</summary>
public interface IEmployeeDigestService
{
    Task<EmployeeDigestPage> ListAsync(int page = 1, int? pageSize = null, CancellationToken ct = default);
}

/// <summary>
/// Composes digests from the same narrative rendering semantic search embeds
/// (<see cref="ChunkProjection"/>): the professional summary plus one block per experience
/// (role @ company, dates, summary, achievement bullets), truncated to a prompt-friendly budget.
/// Pure projection over the employee aggregate — no embeddings involved, so digests exist the
/// moment an employee does. Drafts are excluded (they are hidden from search and staffing too).
/// </summary>
public sealed class EmployeeDigestService(IAppDbContext db) : IEmployeeDigestService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>Keeps one chunk-call's worth of digests prompt-sized; long careers truncate with
    /// an ellipsis rather than bloating the scoring prompt.</summary>
    public const int MaxDigestChars = 1500;

    public async Task<EmployeeDigestPage> ListAsync(
        int page = 1, int? pageSize = null, CancellationToken ct = default)
    {
        var effectivePage = Math.Max(1, page);
        var effectiveSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var active = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        var total = await active.CountAsync(ct);

        var employees = await active
            .Include(e => e.Experiences)
            .ThenInclude(x => x.Achievements)
            .OrderBy(e => e.Id)
            .Skip((effectivePage - 1) * effectiveSize)
            .Take(effectiveSize)
            .ToListAsync(ct);

        var items = employees
            .Select(e => new EmployeeDigest(e.Id, $"{e.FirstName} {e.LastName}", e.Title, Compose(e)))
            .ToList();

        return new EmployeeDigestPage(effectivePage, effectiveSize, total, items);
    }

    private static string Compose(Domain.Entities.Employee employee)
    {
        // The summary + experience chunks are the employee-level narrative units; the
        // per-achievement chunks duplicate bullets already rolled into their experience.
        var blocks = ChunkProjection.Project(employee)
            .Where(c => c.SourceType != SearchChunkSource.Achievement)
            .Select(c => c.Content);
        var digest = string.Join("\n\n", blocks);
        return digest.Length <= MaxDigestChars ? digest : digest[..MaxDigestChars] + "…";
    }
}
