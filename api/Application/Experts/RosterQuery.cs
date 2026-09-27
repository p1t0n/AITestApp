using FluentValidation;

namespace ExpertToJob.Application.Experts;

/// <summary>
/// One screenful of the staff roster (EXP-45): what to match, how to order it, and which slice to
/// return. The whole Roster — Draft, Active and Paused — because this is the list a human accounts
/// for every Expert from; the bench list <see cref="IExpertService.ListAsync"/> serves is a
/// different question and is deliberately left alone.
///
/// <para>Every field is optional and every default lives in <see cref="RosterPaging"/>, so a caller
/// that asks for nothing gets the first page in name order. What a caller may <em>not</em> do is
/// ask for something meaningless: <see cref="RosterQueryValidator"/> rejects an unknown sort key, a
/// page before the first and a page larger than the cap, and it does so in the Application layer so
/// REST and MCP would refuse identically rather than only whichever shell happened to check.</para>
/// </summary>
/// <param name="Q">Case-insensitive <em>contains</em> over full name, email and title.</param>
/// <param name="Sort">One of <see cref="RosterSort.Keys"/>; null means <see cref="RosterSort.Name"/>.</param>
/// <param name="Dir">"asc" or "desc"; null means ascending.</param>
/// <param name="Page">1-based.</param>
/// <param name="PageSize">1..<see cref="RosterPaging.MaxPageSize"/>.</param>
public sealed record RosterQuery(
    string? Q = null,
    string? Sort = null,
    string? Dir = null,
    int? Page = null,
    int? PageSize = null);

/// <summary>
/// The page itself. <c>Total</c> is the size of the whole match, counted in SQL rather than by
/// measuring a materialised list — the roster heading reads "N experts" and the footer prints
/// "Showing from–to of total", and both of those are lies the moment the count is of one page.
/// </summary>
public sealed record RosterPage(IReadOnlyList<ExpertSummaryDto> Items, int Total);

/// <summary>The sort keys the roster offers, as the wire spells them.</summary>
public static class RosterSort
{
    /// <summary>Last name, then first name — the order the roster has always been in.</summary>
    public const string Name = "name";
    public const string Title = "title";
    public const string Location = "location";
    /// <summary>Availability today, from the availability schedule.</summary>
    public const string Capacity = "capacity";
    public const string Status = "status";

    public static readonly IReadOnlyList<string> Keys = [Name, Title, Location, Capacity, Status];

    /// <summary>The key as the service compares it: trimmed, lowered, and null for "the default".
    /// Not a fallback for garbage — the validator has already refused anything not in
    /// <see cref="Keys"/> by the time a service calls this.</summary>
    public static string? Normalize(string? sort) =>
        string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().ToLowerInvariant();
}

/// <summary>Sort directions, as the wire spells them.</summary>
public static class RosterDirection
{
    public const string Ascending = "asc";
    public const string Descending = "desc";

    public static readonly IReadOnlyList<string> Keys = [Ascending, Descending];

    /// <summary>True when the caller asked for descending. Ascending is the default, so anything
    /// the validator let through that is not "desc" is ascending.</summary>
    public static bool IsDescending(string? dir) =>
        string.Equals(dir?.Trim(), Descending, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Where the roster's page bounds are decided, once.</summary>
public static class RosterPaging
{
    /// <summary>What a caller gets when it does not say — a screenful, matching the prototype the
    /// layout came from.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Hard cap, so no caller can ask the roster for everything through the paged door.
    /// Asking for more is an error rather than a silent clamp: a caller that thinks it received
    /// 500 rows and received 100 pages wrongly and never finds out.</summary>
    public const int MaxPageSize = 100;
}

/// <summary>
/// The roster query's rules, at the Application layer with every other validator, so the answer to
/// "is <c>sort=firstname</c> allowed?" does not depend on which shell asked.
/// </summary>
public class RosterQueryValidator : AbstractValidator<RosterQuery>
{
    public RosterQueryValidator()
    {
        RuleFor(q => q.Sort)
            .Must(sort => sort is null || RosterSort.Keys.Contains(RosterSort.Normalize(sort)!))
            .WithMessage($"Sort must be one of: {string.Join(", ", RosterSort.Keys)}.");

        RuleFor(q => q.Dir)
            .Must(dir => dir is null
                         || RosterDirection.Keys.Contains(dir.Trim().ToLowerInvariant()))
            .WithMessage($"Dir must be one of: {string.Join(", ", RosterDirection.Keys)}.");

        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1)
            .When(q => q.Page.HasValue)
            .WithMessage("Page is 1-based; the first page is 1.");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, RosterPaging.MaxPageSize)
            .When(q => q.PageSize.HasValue)
            .WithMessage($"PageSize must be between 1 and {RosterPaging.MaxPageSize}.");
    }
}
