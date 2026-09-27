using ExpertToJob.Domain.Enums;
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
/// <param name="Statuses">Any of <see cref="RosterStatuses.Keys"/>; empty or null means every
/// status. Several are a union, as a group of checkboxes reads (EXP-47).</param>
/// <param name="Locations">Exact location matches, unioned the same way. Free text rather than a
/// closed set, because the roster's locations <em>are</em> whatever the roster holds.</param>
/// <param name="Band">One of <see cref="RosterBand.Keys"/>; null means any availability.</param>
public sealed record RosterQuery(
    string? Q = null,
    string? Sort = null,
    string? Dir = null,
    int? Page = null,
    int? PageSize = null,
    IReadOnlyList<string>? Statuses = null,
    IReadOnlyList<string>? Locations = null,
    string? Band = null);

/// <summary>
/// The page itself. <c>Total</c> is the size of the whole match, counted in SQL rather than by
/// measuring a materialised list — the roster heading reads "N experts" and the footer prints
/// "Showing from–to of total", and both of those are lies the moment the count is of one page.
/// </summary>
public sealed record RosterPage(IReadOnlyList<ExpertSummaryDto> Items, int Total, RosterFacets Facets);

/// <summary>One choice a person can make in the sidebar, and how many rows it would leave.</summary>
public sealed record RosterFacetCount(string Value, int Count);

/// <summary>
/// The counts beside the sidebar's filters (EXP-47), one list per group.
///
/// <para><b>Each group is counted against every other active filter and never against its own.</b>
/// That is the whole point of a facet sidebar and the one thing a naive implementation gets wrong:
/// count Status under the Status filter and every unchecked box reads 0, which tells a person
/// nothing they did not already know. Counted the other way, "Draft (2)" beside a roster already
/// narrowed to Active says exactly what ticking Draft as well would add.</para>
///
/// <para><see cref="Status"/> and <see cref="Band"/> always carry every value, at zero if need be —
/// a checkbox that disappears when its count reaches zero cannot be unchecked back into existence.
/// <see cref="Location"/> carries every location on the caller's roster for the same reason, which
/// is why a zero there is a row to grey out rather than a row to drop.</para>
/// </summary>
public sealed record RosterFacets(
    IReadOnlyList<RosterFacetCount> Status,
    IReadOnlyList<RosterFacetCount> Band,
    IReadOnlyList<RosterFacetCount> Location);

/// <summary>
/// The availability bands the sidebar offers, as the wire spells them, and the capacity range each
/// one covers. Three named buckets rather than a number pair on the query: "who is free today" is
/// the question the roster is actually asked, and a range control invites the other one.
/// </summary>
public static class RosterBand
{
    /// <summary>100% — free all day.</summary>
    public const string Full = "full";
    /// <summary>1–99% — some of the day.</summary>
    public const string Partial = "partial";
    /// <summary>0%, including somebody with no schedule at all.</summary>
    public const string None = "none";

    /// <summary>Most available first, which is the order the sidebar reads in.</summary>
    public static readonly IReadOnlyList<string> Keys = [Full, Partial, None];

    /// <summary>The band as the service compares it: trimmed, lowered, null for "any". The
    /// validator has already refused anything not in <see cref="Keys"/> by the time this is used
    /// to choose a range.</summary>
    public static string? Normalize(string? band) =>
        string.IsNullOrWhiteSpace(band) ? null : band.Trim().ToLowerInvariant();

    /// <summary>The inclusive capacity range a band covers. <see cref="Full"/> reaches past 100 on
    /// purpose: the band means "nothing booked", and a schedule that somehow says 120 is not
    /// suddenly a partial day.</summary>
    public static (int Min, int Max) Range(string band) => band switch
    {
        Full => (100, int.MaxValue),
        Partial => (1, 99),
        None => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Not a roster band."),
    };
}

/// <summary>The statuses the sidebar offers, as the wire spells them.</summary>
public static class RosterStatuses
{
    /// <summary>Every <see cref="ExpertStatus"/>, in the order the sidebar lists them: the
    /// published people first, then the ones still waiting at the gate.</summary>
    public static readonly IReadOnlyList<string> Keys =
        [nameof(ExpertStatus.Active), nameof(ExpertStatus.Draft)];

    /// <summary>
    /// A status name off the wire, trimmed and case-insensitive. Deliberately <em>not</em>
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>, which also accepts "2" and
    /// "Draft, Active": the number is a storage detail that must not become a public spelling, and
    /// a comma-separated pair silently means something no caller asked for.
    /// </summary>
    public static bool TryParse(string? value, out ExpertStatus status)
    {
        status = default;
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return false;

        foreach (var candidate in Enum.GetValues<ExpertStatus>())
        {
            if (!string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                continue;
            status = candidate;
            return true;
        }
        return false;
    }

    /// <summary>The statuses a query actually filters on: parsed, de-duplicated, blanks dropped.
    /// An empty result is "every status", which is what an untouched group of checkboxes means.</summary>
    public static IReadOnlyList<ExpertStatus> Selected(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return [];
        var chosen = new List<ExpertStatus>();
        foreach (var value in values)
        {
            if (TryParse(value, out var status) && !chosen.Contains(status)) chosen.Add(status);
        }
        return chosen;
    }
}

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

        // A status the roster does not have is a 400 rather than a filter that quietly matches
        // nobody: an empty roster and a misspelled filter look identical on screen, and only one of
        // them is the caller's fault.
        RuleFor(q => q.Statuses)
            .Must(statuses => statuses is null || statuses.All(IsBlankOrKnownStatus))
            .WithMessage($"Statuses must each be one of: {string.Join(", ", RosterStatuses.Keys)}.");

        RuleFor(q => q.Band)
            .Must(band => band is null || RosterBand.Keys.Contains(RosterBand.Normalize(band)!))
            .WithMessage($"Band must be one of: {string.Join(", ", RosterBand.Keys)}.");

        // Locations are deliberately not validated. There is no closed set to check against — the
        // roster's places are whatever its rows say — so a bookmark naming a city the last person
        // left shows an empty roster with the sidebar's way out still on screen, not a 400.
    }

    /// <summary>A blank entry is what an empty <c>?statuses=</c> binds to; it asks for nothing and
    /// is filtered out later, so it is not an error.</summary>
    private static bool IsBlankOrKnownStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) || RosterStatuses.TryParse(value, out _);
}
