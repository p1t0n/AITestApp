using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The roster's sidebar filters and the counts beside them (EXP-47).
///
/// <para>Two rules are under test and they are easy to conflate. The <em>filter</em> rule is the
/// obvious one: a checkbox narrows the rows. The <em>count</em> rule is the one that makes a facet
/// sidebar usable and the one a naive implementation always gets wrong — each group's counts are
/// computed against every <em>other</em> active filter and never against its own, so a person who
/// has already chosen "Active" can still see how many Drafts choosing Draft as well would add. A
/// count that obeyed its own group would read 0 for every unchecked box and the sidebar would tell
/// you nothing you did not already know.</para>
///
/// <para>EF InMemory again, for the same reason as <see cref="RosterSearchTests"/>: these are the
/// rules. That the counting is the database's work and not a materialised list's is
/// <c>Web.Tests/RosterFacetEndpointTests</c>'s job, against real Postgres.</para>
/// </summary>
public class RosterFacetTests
{
    private sealed class PinnedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 9, 27);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"roster-facets-{Guid.NewGuid()}")
            .Options);

    private static ExpertService NewService(
        AppDbContext db, RosterAudience audience = RosterAudience.Administration) =>
        new(db, new SaveExpertValidator(), new UpdateExpertValidator(), new RosterQueryValidator(),
            new UnrestrictedOwnershipScopeProvider(), new FixedAudience(audience),
            new PinnedClock(Today));

    private sealed class FixedAudience(RosterAudience audience) : IRosterAudienceProvider
    {
        public RosterAudience Current => audience;
    }

    private static Expert Person(
        string first,
        string last,
        string? location = "Vilnius",
        int capacityToday = 0,
        ExpertStatus status = ExpertStatus.Active,
        DateTimeOffset? hiddenAt = null,
        string title = "Engineer")
    {
        var e = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Title = title,
            Location = location,
            Email = $"{first}.{last}@example.com".ToLowerInvariant(),
            Status = status,
            HiddenAt = hiddenAt,
        };

        // A stale entry the step function must not pick, and the one in force today. A person left
        // at 0 gets no schedule at all, which is the other way to be unavailable and must land in
        // the same band.
        if (capacityToday > 0)
        {
            e.AvailabilityEntries.Add(new AvailabilityEntry
            {
                Id = Guid.NewGuid(),
                ExpertId = e.Id,
                EffectiveFrom = Today.AddYears(-1),
                CapacityPercent = 7,
            });
            e.AvailabilityEntries.Add(new AvailabilityEntry
            {
                Id = Guid.NewGuid(),
                ExpertId = e.Id,
                EffectiveFrom = Today,
                CapacityPercent = capacityToday,
            });
        }

        return e;
    }

    private static async Task SeedAsync(AppDbContext db, params Expert[] people)
    {
        db.Experts.AddRange(people);
        await db.SaveChangesAsync();
    }

    private static string[] Names(RosterPage page) =>
        page.Items.Select(i => $"{i.FirstName} {i.LastName}").ToArray();

    private static int Count(IReadOnlyList<RosterFacetCount> facet, string value) =>
        facet.Single(f => f.Value == value).Count;

    /// <summary>
    /// Six people who disagree on every facet at once, so no assertion below can pass by accident:
    /// two locations, both statuses, and all three availability bands including both boundaries.
    /// </summary>
    private static Expert[] TheRoster() =>
    [
        Person("Ada", "Lovelace", location: "London", capacityToday: 100),
        Person("Alan", "Turing", location: "London", capacityToday: 0),
        Person("Barbara", "Liskov", location: "London", capacityToday: 50,
            status: ExpertStatus.Draft),
        Person("Grace", "Hopper", location: "Arlington", capacityToday: 100,
            status: ExpertStatus.Draft),
        Person("Donald", "Knuth", location: "Arlington", capacityToday: 99),
        Person("Katherine", "Johnson", location: null, capacityToday: 1),
    ];

    // ---- the filters themselves --------------------------------------------------------------

    [Fact]
    public async Task A_status_filter_narrows_the_roster_to_that_status()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Statuses: ["Draft"]));

        Names(page).Should().BeEquivalentTo(["Barbara Liskov", "Grace Hopper"]);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task Two_statuses_checked_is_the_union_of_both()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Statuses: ["Draft", "Active"]));

        page.Total.Should().Be(6, "checking every box asks for everything, not for nothing");
    }

    [Fact]
    public async Task A_location_filter_narrows_the_roster_to_those_locations()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Locations: ["Arlington"]));

        Names(page).Should().BeEquivalentTo(["Grace Hopper", "Donald Knuth"]);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task Somebody_with_no_location_is_in_no_location_bucket()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db)
            .SearchAsync(new RosterQuery(Locations: ["London", "Arlington"]));

        Names(page).Should().NotContain("Katherine Johnson",
            "a blank location is not a place, so it joins no place's checkbox");
        page.Total.Should().Be(5);
    }

    [Theory]
    [InlineData(RosterBand.Full, new[] { "Ada Lovelace", "Grace Hopper" })]
    [InlineData(RosterBand.Partial, new[] { "Barbara Liskov", "Donald Knuth", "Katherine Johnson" })]
    [InlineData(RosterBand.None, new[] { "Alan Turing" })]
    public async Task The_availability_band_splits_at_zero_and_at_a_hundred(string band, string[] expected)
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Band: band));

        Names(page).Should().BeEquivalentTo(expected,
            "1 and 99 are Partial; only 100 is Full and only 0 is Unavailable");
        page.Total.Should().Be(expected.Length);
    }

    [Fact]
    public async Task Somebody_with_no_availability_schedule_at_all_is_Unavailable()
    {
        await using var db = NewDb();
        await SeedAsync(db, Person("Never", "Scheduled", capacityToday: 0));

        var page = await NewService(db).SearchAsync(new RosterQuery(Band: RosterBand.None));

        Names(page).Should().Equal(["Never Scheduled"],
            "before the first entry capacity is 0, which is a band and not an absence");
    }

    [Fact]
    public async Task Filters_combine_with_each_other_and_with_the_search()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(
            Q: "engineer",
            Statuses: ["Active"],
            Locations: ["London"],
            Band: RosterBand.Full));

        Names(page).Should().Equal(["Ada Lovelace"], "every clause narrows, none of them widens");
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task An_empty_filter_list_is_no_filter_at_all()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db)
            .SearchAsync(new RosterQuery(Statuses: [], Locations: [], Band: null));

        page.Total.Should().Be(6, "nothing checked means the whole roster, not an empty one");
    }

    // ---- the counts beside them --------------------------------------------------------------

    [Fact]
    public async Task With_nothing_chosen_every_count_is_of_the_whole_roster()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery())).Facets;

        Count(facets.Status, "Active").Should().Be(4);
        Count(facets.Status, "Draft").Should().Be(2);
        Count(facets.Band, RosterBand.Full).Should().Be(2);
        Count(facets.Band, RosterBand.Partial).Should().Be(3);
        Count(facets.Band, RosterBand.None).Should().Be(1);
        Count(facets.Location, "London").Should().Be(3);
        Count(facets.Location, "Arlington").Should().Be(2);
    }

    [Fact]
    public async Task A_status_choice_does_not_narrow_the_status_counts()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery(Statuses: ["Active"]))).Facets;

        Count(facets.Status, "Active").Should().Be(4);
        Count(facets.Status, "Draft").Should().Be(2,
            "the Draft count has to say what checking Draft as well would add, or it says nothing");
    }

    [Fact]
    public async Task A_status_choice_does_narrow_every_other_group()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery(Statuses: ["Draft"]))).Facets;

        Count(facets.Location, "London").Should().Be(1, "only Barbara is a London Draft");
        Count(facets.Location, "Arlington").Should().Be(1);
        Count(facets.Band, RosterBand.Full).Should().Be(1, "Grace");
        Count(facets.Band, RosterBand.Partial).Should().Be(1, "Barbara");
        Count(facets.Band, RosterBand.None).Should().Be(0);
    }

    [Fact]
    public async Task A_location_choice_does_not_narrow_the_location_counts()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery(Locations: ["London"]))).Facets;

        Count(facets.Location, "London").Should().Be(3);
        Count(facets.Location, "Arlington").Should().Be(2,
            "Arlington still says what adding it to the selection would give");
        Count(facets.Status, "Active").Should().Be(2, "but the status counts are London's");
        Count(facets.Status, "Draft").Should().Be(1);
    }

    [Fact]
    public async Task A_band_choice_does_not_narrow_the_band_counts()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery(Band: RosterBand.Full))).Facets;

        Count(facets.Band, RosterBand.Full).Should().Be(2);
        Count(facets.Band, RosterBand.Partial).Should().Be(3);
        Count(facets.Band, RosterBand.None).Should().Be(1);
        Count(facets.Status, "Active").Should().Be(1, "but the status counts are Full's: Ada");
        Count(facets.Status, "Draft").Should().Be(1, "and Grace");
    }

    [Fact]
    public async Task Every_group_obeys_the_search()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Q: "lovelace"));

        Count(page.Facets.Status, "Active").Should().Be(1);
        Count(page.Facets.Status, "Draft").Should().Be(0);
        Count(page.Facets.Band, RosterBand.Full).Should().Be(1);
        Count(page.Facets.Band, RosterBand.None).Should().Be(0);
        // The search is the sidebar's coarse cut and the location list follows it, rather than
        // offering every place on the roster at zero next to the one name that matched.
        page.Facets.Location.Should().Equal([new RosterFacetCount("London", 1)]);
    }

    [Fact]
    public async Task A_location_already_ticked_stays_on_the_list_even_when_the_search_excludes_it()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db)
            .SearchAsync(new RosterQuery(Q: "lovelace", Locations: ["Arlington"]));

        page.Total.Should().Be(0, "nobody in Arlington is called Lovelace");
        Count(page.Facets.Location, "Arlington").Should().Be(0,
            "a filter that vanishes from the sidebar is one nobody can undo");
    }

    [Fact]
    public async Task Two_groups_chosen_and_each_of_them_still_counts_without_itself()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db)
            .SearchAsync(new RosterQuery(Statuses: ["Active"], Locations: ["London"]))).Facets;

        // Status counts come from "London" alone: Ada and Alan are Active there, Barbara is a Draft.
        Count(facets.Status, "Active").Should().Be(2);
        Count(facets.Status, "Draft").Should().Be(1);
        // Location counts come from "Active" alone: Ada and Alan in London, Donald in Arlington.
        Count(facets.Location, "London").Should().Be(2);
        Count(facets.Location, "Arlington").Should().Be(1);
        // And the band counts come from both, which is every group but their own.
        Count(facets.Band, RosterBand.Full).Should().Be(1, "Ada");
        Count(facets.Band, RosterBand.None).Should().Be(1, "Alan");
        Count(facets.Band, RosterBand.Partial).Should().Be(0);
    }

    // ---- the shape of the sidebar ------------------------------------------------------------

    [Fact]
    public async Task Both_statuses_and_all_three_bands_are_always_listed_even_at_zero()
    {
        await using var db = NewDb();
        await SeedAsync(db, Person("Only", "Person", location: "London", capacityToday: 100));

        var facets = (await NewService(db).SearchAsync(new RosterQuery())).Facets;

        facets.Status.Select(f => f.Value).Should().Equal(["Active", "Draft"],
            "a checkbox that vanishes at zero cannot be unchecked back into view");
        facets.Band.Select(f => f.Value)
            .Should().Equal([RosterBand.Full, RosterBand.Partial, RosterBand.None]);
        Count(facets.Status, "Draft").Should().Be(0);
        Count(facets.Band, RosterBand.None).Should().Be(0);
    }

    [Fact]
    public async Task A_location_narrowed_to_zero_is_still_listed_so_it_can_be_chosen_back()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var facets = (await NewService(db).SearchAsync(new RosterQuery(Band: RosterBand.None))).Facets;

        // Only Alan is Unavailable, so Arlington's count under that band is zero — and the row has
        // to stay, greyed out, or widening the band is the only way to discover Arlington exists.
        Count(facets.Location, "London").Should().Be(1);
        Count(facets.Location, "Arlington").Should().Be(0);
    }

    [Fact]
    public async Task Locations_are_ordered_by_count_with_the_name_breaking_ties()
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("A", "One", location: "Zurich"),
            Person("B", "Two", location: "Zurich"),
            Person("C", "Three", location: "Athens"),
            Person("D", "Four", location: "Berlin"));

        var facets = (await NewService(db).SearchAsync(new RosterQuery())).Facets;

        facets.Location.Select(f => f.Value).Should().Equal(["Zurich", "Athens", "Berlin"],
            "the busiest place first, then alphabetically — a scrollable list needs a fixed order");
    }

    [Fact]
    public async Task A_blank_location_is_not_a_facet_row()
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("Ada", "Lovelace", location: "London"),
            Person("Nowhere", "Man", location: null),
            Person("Empty", "String", location: ""));

        var facets = (await NewService(db).SearchAsync(new RosterQuery())).Facets;

        facets.Location.Select(f => f.Value).Should().Equal(["London"]);
    }

    [Fact]
    public async Task The_facets_obey_the_audience_the_rows_do()
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("Ada", "Lovelace", location: "London", capacityToday: 100),
            Person("Self", "Pausewell", location: "London", capacityToday: 100,
                hiddenAt: DateTimeOffset.UtcNow));

        var facets = (await NewService(db, RosterAudience.Bench).SearchAsync(new RosterQuery())).Facets;

        Count(facets.Location, "London").Should().Be(1,
            "a count that included a row the caller cannot see is a count of somebody else's roster");
        Count(facets.Band, RosterBand.Full).Should().Be(1);
    }

    // ---- what the validator refuses ----------------------------------------------------------

    [Theory]
    [InlineData("Paused", "is not a status at all — a pause is a timestamp on an Active row")]
    [InlineData("2", "the wire spells a status by name; the enum's number is an implementation detail")]
    [InlineData("Archived", "and nothing invented elsewhere is quietly dropped either")]
    public async Task An_unknown_status_is_refused_rather_than_ignored(string status, string why)
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var act = () => NewService(db).SearchAsync(new RosterQuery(Statuses: [status]));

        (await act.Should().ThrowAsync<ValidationException>(why))
            .Which.Message.Should().Contain("Statuses");
    }

    [Fact]
    public async Task A_status_is_accepted_however_it_is_spelled()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        // Trimmed and case-insensitive is a spelling, not a different filter.
        var page = await NewService(db).SearchAsync(new RosterQuery(Statuses: [" draft "]));

        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task An_unknown_band_is_refused_rather_than_ignored()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var act = () => NewService(db).SearchAsync(new RosterQuery(Band: "mostly"));

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Message.Should().Contain("Band");
    }

    [Fact]
    public async Task A_band_is_accepted_however_it_is_spelled()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db).SearchAsync(new RosterQuery(Band: " FULL "));

        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task A_location_nobody_is_in_matches_nobody_rather_than_failing()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        // Locations are free text off the roster itself, so there is no closed set to validate
        // against — a stale bookmark naming a place the last person left shows an empty roster.
        var page = await NewService(db).SearchAsync(new RosterQuery(Locations: ["Atlantis"]));

        page.Total.Should().Be(0);
        Count(page.Facets.Location, "London").Should().Be(3, "and the sidebar still offers the way out");
        Count(page.Facets.Location, "Atlantis").Should().Be(0,
            "including unticking the place that emptied the roster");
    }

    // ---- paging, still --------------------------------------------------------------------------

    [Fact]
    public async Task A_filtered_total_is_the_filtered_total_and_the_page_is_cut_from_it()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheRoster());

        var page = await NewService(db)
            .SearchAsync(new RosterQuery(Locations: ["London"], PageSize: 2, Page: 2));

        page.Total.Should().Be(3, "the footer counts the match, not the roster and not the page");
        page.Items.Should().HaveCount(1);
    }
}
