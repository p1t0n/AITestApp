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
/// The staff roster's own query (EXP-45): search, sort and page, over the whole Roster.
///
/// <para>These run against EF InMemory, which is the right level for the <em>rules</em> — which
/// rows match, what order they come back in, where a page starts and stops. What it cannot show is
/// that any of it reaches SQL rather than a materialised list, because InMemory has no SQL; that is
/// <c>Web.Tests/RosterSearchEndpointTests</c>'s job, against a real Postgres.</para>
/// </summary>
public class RosterSearchTests
{
    /// <summary>A clock pinned to one instant, so "availability today" is a fact of the test and
    /// not of the day it runs (the lesson of <see cref="RosterCapacityClockTests"/>).</summary>
    private sealed class PinnedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 9, 27);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"roster-search-{Guid.NewGuid()}")
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
        string title = "Engineer",
        string? location = "Vilnius",
        string? email = null,
        ExpertStatus status = ExpertStatus.Active,
        DateTimeOffset? hiddenAt = null,
        int? capacityToday = null)
    {
        var e = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Title = title,
            Location = location,
            Email = email ?? $"{first}.{last}@example.com".ToLowerInvariant(),
            Status = status,
            HiddenAt = hiddenAt,
        };

        if (capacityToday is not null)
        {
            // Two entries, so the step function has something to choose between: a stale one that
            // must not win, and the one in force today.
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
                CapacityPercent = capacityToday.Value,
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

    // ---- search ------------------------------------------------------------------------------

    [Theory]
    [InlineData("lovelace", "matches the last name")]
    [InlineData("ada", "matches the first name")]
    [InlineData("ada love", "matches across the space, because the name is matched as one string")]
    [InlineData("LOVELACE", "is case-insensitive")]
    [InlineData("countess@example.com", "matches the email")]
    [InlineData("analytical", "matches inside the title")]
    public async Task Search_matches_name_email_and_title(string needle, string why)
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("Ada", "Lovelace", title: "Analytical Engineer", email: "countess@example.com"),
            Person("Grace", "Hopper", title: "Rear Admiral", email: "grace@navy.example"));

        var page = await NewService(db).SearchAsync(new RosterQuery(Q: needle));

        Names(page).Should().Equal(["Ada Lovelace"], why);
        page.Total.Should().Be(1, "the total counts the match, not the roster");
    }

    [Fact]
    public async Task Search_matches_nothing_it_was_not_asked_for()
    {
        await using var db = NewDb();
        await SeedAsync(db, Person("Ada", "Lovelace"), Person("Grace", "Hopper"));

        var page = await NewService(db).SearchAsync(new RosterQuery(Q: "babbage"));

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_or_blank_search_is_no_search_at_all()
    {
        await using var db = NewDb();
        await SeedAsync(db, Person("Ada", "Lovelace"), Person("Grace", "Hopper"));

        foreach (var q in new string?[] { null, "", "   " })
        {
            (await NewService(db).SearchAsync(new RosterQuery(Q: q))).Total
                .Should().Be(2, $"'{q ?? "null"}' asks for the whole roster");
        }
    }

    // ---- what is on the list -----------------------------------------------------------------

    [Fact]
    public async Task The_whole_Roster_is_listed_Draft_Active_and_Paused()
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("Staged", "Draftly", status: ExpertStatus.Draft),
            Person("Published", "Activeson"),
            Person("Self", "Pausewell", hiddenAt: DateTimeOffset.UtcNow));

        var page = await NewService(db).SearchAsync(new RosterQuery());

        Names(page).Should().BeEquivalentTo(["Staged Draftly", "Published Activeson", "Self Pausewell"]);
        page.Total.Should().Be(3);
    }

    [Fact]
    public async Task A_paused_Expert_is_hidden_from_a_bench_audience_through_this_query_too()
    {
        await using var db = NewDb();
        await SeedAsync(db,
            Person("Staged", "Draftly", status: ExpertStatus.Draft),
            Person("Published", "Activeson"),
            Person("Self", "Pausewell", hiddenAt: DateTimeOffset.UtcNow));

        // The same seam as every other roster read (P1T-185), and the same division of labour: the
        // audience decides who sees somebody who paused themselves, the caller decides whether it
        // wants Drafts. So a host looking at the roster as the bench loses the paused row here
        // exactly as it does through ListAsync, and the new query adds no second answer.
        var page = await NewService(db, RosterAudience.Bench).SearchAsync(new RosterQuery());

        Names(page).Should().BeEquivalentTo(["Staged Draftly", "Published Activeson"]);
        page.Total.Should().Be(2, "the total obeys the visibility rules too, or the footer lies");
    }

    [Fact]
    public async Task An_owner_scoped_caller_reaches_only_their_own_row()
    {
        await using var db = NewDb();
        var mine = Person("Ada", "Lovelace");
        await SeedAsync(db, mine, Person("Grace", "Hopper"));

        var svc = new ExpertService(
            db, new SaveExpertValidator(), new UpdateExpertValidator(), new RosterQueryValidator(),
            new FixedScope(OwnershipScope.OwnedBy(mine.Id)), new AdministrationAudienceProvider(),
            new PinnedClock(Today));

        var page = await svc.SearchAsync(new RosterQuery());

        Names(page).Should().Equal(["Ada Lovelace"]);
        page.Total.Should().Be(1);
    }

    private sealed class FixedScope(OwnershipScope scope) : IOwnershipScopeProvider
    {
        public ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default) => new(scope);
    }

    // ---- sort --------------------------------------------------------------------------------

    /// <summary>Five people whose every sortable field orders them differently, so no assertion
    /// below can pass by accident on the seed order.</summary>
    private static Expert[] TheFive() =>
    [
        Person("Ada", "Lovelace", title: "Analytical Engineer", location: "London",
            status: ExpertStatus.Active, capacityToday: 50),
        Person("Grace", "Hopper", title: "Rear Admiral", location: "Arlington",
            status: ExpertStatus.Draft, capacityToday: 100),
        Person("Alan", "Turing", title: "Cryptanalyst", location: "Wilmslow",
            status: ExpertStatus.Active, capacityToday: 0),
        Person("Katherine", "Johnson", title: "Mathematician", location: null,
            status: ExpertStatus.Active, capacityToday: 80),
        Person("Edsger", "Dijkstra", title: "Professor", location: "Rotterdam",
            status: ExpertStatus.Draft, capacityToday: 20),
    ];

    [Fact]
    public async Task Name_sorts_by_last_name_then_first_in_both_directions()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var asc = await svc.SearchAsync(new RosterQuery(Sort: "name", Dir: "asc"));
        var desc = await svc.SearchAsync(new RosterQuery(Sort: "name", Dir: "desc"));

        asc.Items.Select(i => i.LastName).Should()
            .Equal(["Dijkstra", "Hopper", "Johnson", "Lovelace", "Turing"]);
        desc.Items.Select(i => i.LastName).Should().Equal(asc.Items.Select(i => i.LastName).Reverse());
    }

    [Fact]
    public async Task Name_is_also_the_default_sort_and_the_default_direction_is_ascending()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var implicitly_ = await svc.SearchAsync(new RosterQuery());
        var explicitly = await svc.SearchAsync(new RosterQuery(Sort: "name", Dir: "asc"));

        Names(implicitly_).Should().Equal(Names(explicitly));
    }

    [Fact]
    public async Task Title_sorts_in_both_directions()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var asc = await svc.SearchAsync(new RosterQuery(Sort: "title", Dir: "asc"));
        var desc = await svc.SearchAsync(new RosterQuery(Sort: "title", Dir: "desc"));

        asc.Items.Select(i => i.Title).Should()
            .Equal(["Analytical Engineer", "Cryptanalyst", "Mathematician", "Professor", "Rear Admiral"]);
        desc.Items.Select(i => i.Title).Should().Equal(asc.Items.Select(i => i.Title).Reverse());
    }

    [Fact]
    public async Task Location_sorts_in_both_directions_with_a_missing_location_reading_as_empty()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var asc = await svc.SearchAsync(new RosterQuery(Sort: "location", Dir: "asc"));
        var desc = await svc.SearchAsync(new RosterQuery(Sort: "location", Dir: "desc"));

        // Katherine Johnson has no location; it sorts as "" and so leads ascending, which is a
        // decision rather than a provider's NULL convention leaking through.
        asc.Items.Select(i => i.Location).Should()
            .Equal([null, "Arlington", "London", "Rotterdam", "Wilmslow"]);
        desc.Items.Select(i => i.Location).Should().Equal(asc.Items.Select(i => i.Location).Reverse());
    }

    [Fact]
    public async Task Capacity_sorts_by_availability_today_in_both_directions()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var most = await svc.SearchAsync(new RosterQuery(Sort: "capacity", Dir: "desc"));
        var least = await svc.SearchAsync(new RosterQuery(Sort: "capacity", Dir: "asc"));

        // The percentages the page reports are the ones it ordered by — the SQL-side step function
        // and the in-memory one agreeing, over a schedule whose earlier entry is a decoy.
        most.Items.Select(i => i.CurrentCapacityPercent).Should().Equal([100, 80, 50, 20, 0]);
        least.Items.Select(i => i.CurrentCapacityPercent).Should().Equal([0, 20, 50, 80, 100]);
    }

    [Fact]
    public async Task Status_sorts_Drafts_first_ascending_and_reverses()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var asc = await svc.SearchAsync(new RosterQuery(Sort: "status", Dir: "asc"));
        var desc = await svc.SearchAsync(new RosterQuery(Sort: "status", Dir: "desc"));

        asc.Items.Select(i => i.Status).Should().Equal(
            [ExpertStatus.Draft, ExpertStatus.Draft, ExpertStatus.Active, ExpertStatus.Active,
             ExpertStatus.Active],
            "unpublished work leads the list a human is meant to clear");
        desc.Items.Select(i => i.Status).Should().Equal(asc.Items.Select(i => i.Status).Reverse());
    }

    [Fact]
    public async Task A_sort_key_two_rows_tie_on_is_broken_by_name_and_then_by_id()
    {
        await using var db = NewDb();
        // Every one of these ties on title, location, capacity and status: the tiebreak is the
        // only thing deciding the order, in every direction.
        await SeedAsync(db,
            Person("Zoe", "Alpha"), Person("Ann", "Alpha"), Person("Bob", "Beta"));
        var svc = NewService(db);

        foreach (var dir in new[] { "asc", "desc" })
        {
            var page = await svc.SearchAsync(new RosterQuery(Sort: "title", Dir: dir));

            Names(page).Should().Equal(["Ann Alpha", "Zoe Alpha", "Bob Beta"],
                $"a tie on title falls through to last name, then first name — {dir} or not");
        }
    }

    [Fact]
    public async Task Paging_a_tied_list_repeats_nothing_and_skips_nothing()
    {
        await using var db = NewDb();
        // Twelve rows identical in everything the sort can see. Without a total order, the page
        // boundaries here are the database's whim and a row can appear twice or not at all.
        await SeedAsync(db, Enumerable.Range(0, 12)
            .Select(i => Person("Same", "Person", email: $"same{i}@example.com")).ToArray());
        var svc = NewService(db);

        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await svc.SearchAsync(new RosterQuery(Sort: "title", Page: page, PageSize: 4));
            slice.Items.Should().HaveCount(4);
            ids.AddRange(slice.Items.Select(i => i.Id));
        }

        ids.Should().OnlyHaveUniqueItems("a stable order never hands the same row out twice");
        ids.Should().HaveCount(12, "and never loses one either");
    }

    // ---- paging ------------------------------------------------------------------------------

    [Fact]
    public async Task A_page_is_a_slice_of_the_match_and_the_total_is_the_whole_of_it()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());
        var svc = NewService(db);

        var first = await svc.SearchAsync(new RosterQuery(Page: 1, PageSize: 2));
        var second = await svc.SearchAsync(new RosterQuery(Page: 2, PageSize: 2));
        var third = await svc.SearchAsync(new RosterQuery(Page: 3, PageSize: 2));

        first.Items.Select(i => i.LastName).Should().Equal(["Dijkstra", "Hopper"]);
        second.Items.Select(i => i.LastName).Should().Equal(["Johnson", "Lovelace"]);
        third.Items.Select(i => i.LastName).Should().Equal(["Turing"], "the last page is short");
        new[] { first, second, third }.Should().OnlyContain(p => p.Total == 5,
            "every page reports the size of the whole match");
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_and_still_reports_the_total()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());

        var page = await NewService(db).SearchAsync(new RosterQuery(Page: 99, PageSize: 2));

        page.Items.Should().BeEmpty("there is no ninety-ninth page");
        page.Total.Should().Be(5, "which is how the SPA can tell somebody they have run off the end");
    }

    [Fact]
    public async Task The_default_page_size_is_twenty_five()
    {
        await using var db = NewDb();
        await SeedAsync(db, Enumerable.Range(0, 30)
            .Select(i => Person("Person", $"Number{i:00}")).ToArray());

        var page = await NewService(db).SearchAsync(new RosterQuery());

        page.Items.Should().HaveCount(RosterPaging.DefaultPageSize).And.HaveCount(25);
        page.Total.Should().Be(30);
    }

    [Fact]
    public async Task Search_and_paging_compose_so_the_total_is_of_the_search()
    {
        await using var db = NewDb();
        await SeedAsync(db, Enumerable.Range(0, 10)
            .Select(i => Person(i < 6 ? "Match" : "Other", $"Person{i:00}")).ToArray());

        var page = await NewService(db).SearchAsync(new RosterQuery(Q: "match", Page: 2, PageSize: 4));

        page.Items.Should().HaveCount(2, "six matches, four on the first page");
        page.Total.Should().Be(6);
    }

    // ---- validation --------------------------------------------------------------------------

    [Theory]
    [InlineData("firstname", null, null, null)]
    [InlineData("", null, null, null)] // only null means "the default"; empty is a caller's mistake
    [InlineData(null, "sideways", null, null)]
    [InlineData(null, null, 0, null)]
    [InlineData(null, null, -1, null)]
    [InlineData(null, null, null, 0)]
    [InlineData(null, null, null, 101)]
    public async Task A_query_the_roster_cannot_answer_is_refused(
        string? sort, string? dir, int? page, int? pageSize)
    {
        await using var db = NewDb();

        var act = () => NewService(db).SearchAsync(new RosterQuery(null, sort, dir, page, pageSize));

        // The Application layer's ValidationException, which the Web host renders as a 400 — so
        // REST and MCP would refuse this identically rather than only whichever shell checked.
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("title")]
    [InlineData("location")]
    [InlineData("capacity")]
    [InlineData("status")]
    [InlineData("NAME")]
    [InlineData(" name ")]
    public async Task Every_advertised_sort_key_is_accepted_however_it_is_cased(string sort)
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());

        var act = () => NewService(db).SearchAsync(new RosterQuery(Sort: sort));

        await act.Should().NotThrowAsync("the keys the SPA's dropdown offers are the keys this takes");
    }

    [Fact]
    public async Task The_largest_page_the_cap_allows_is_accepted()
    {
        await using var db = NewDb();

        var act = () => NewService(db).SearchAsync(new RosterQuery(PageSize: RosterPaging.MaxPageSize));

        await act.Should().NotThrowAsync("the boundary is inclusive, and 101 is the first refusal");
    }

    /// <summary>Keeps the refusals above honest about <em>which</em> input they are refusing: a
    /// validator that failed everything would pass every case in the theory.</summary>
    [Fact]
    public async Task A_fully_specified_valid_query_is_accepted()
    {
        await using var db = NewDb();
        await SeedAsync(db, TheFive());

        var page = await NewService(db)
            .SearchAsync(new RosterQuery("a", RosterSort.Capacity, RosterDirection.Descending, 1, 100));

        page.Total.Should().BeGreaterThan(0);
    }
}
