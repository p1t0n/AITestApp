using System.Net;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The sidebar's filters and their counts, against real Postgres (EXP-47).
///
/// <para>Which rows a filter leaves and which numbers sit beside the checkboxes are settled over
/// EF InMemory in <c>Application.Tests/RosterFacetTests</c>. What only Postgres can answer is
/// here: that the counting is a <c>GROUP BY</c> and three <c>COUNT</c>s the database ran, not
/// arithmetic over a list the service materialised first — and that the availability bands, which
/// are a correlated subquery compared against two bounds, translate to SQL at all.</para>
///
/// <para>Same marker discipline as <c>RosterSearchEndpointTests</c>: the suite shares one database,
/// so every row seeded here carries a nonsense word in its title and every request searches for it.
/// That matters more for this class than for that one — the location list follows the search, so
/// the marker is what keeps another class's cities out of these assertions.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RosterFacetEndpointTests(WebApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private readonly string _marker = $"zz{Guid.NewGuid():N}";

    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    /// <summary>A location with a comma in it, which is why the wire format is repeated keys
    /// rather than one joined value: joined, this would come back as two places nobody is in.</summary>
    private const string Comma = "Cambridge, MA";

    /// <summary>
    /// Seven people who disagree on every facet at once: three cities and a row with none, both
    /// statuses, and all three availability bands including both boundaries (1 and 99 are Partial,
    /// only 100 is Full, only 0 is Unavailable).
    /// </summary>
    private readonly (string Last, string? Location, int Capacity, ExpertStatus Status)[] _people =
    [
        ("Lovelace", "London", 100, ExpertStatus.Active),
        ("Turing", "London", 0, ExpertStatus.Active),
        ("Liskov", "London", 50, ExpertStatus.Draft),
        ("Hopper", "Arlington", 100, ExpertStatus.Draft),
        ("Knuth", "Arlington", 99, ExpertStatus.Active),
        ("Johnson", null, 1, ExpertStatus.Active),
        ("Stroustrup", Comma, 100, ExpertStatus.Active),
    ];

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (last, location, capacity, status) in _people)
        {
            var expert = new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = "Facet",
                LastName = last,
                Title = $"{_marker} Engineer",
                Location = location,
                Email = ApiClientExtensions.UniqueEmail($"facet{last}".ToLowerInvariant()),
                Status = status,
            };
            // These rows join the shared database and `ProcessingRecordDatabaseTests` audits every
            // Expert in it, so the lawful-basis record is not optional however the row got here.
            expert.ProcessingRecords.Add(ProcessingRecord.For(
                expert.Id, sequence: 1, ProcessingOrigin.StaffCreated,
                noticeVersion: null, "Seeded by RosterFacetEndpointTests.", DateTimeOffset.UtcNow));
            // A 0% person gets no schedule at all — the other way to be Unavailable, and the one
            // the coalesce in the capacity subquery exists for.
            if (capacity > 0)
            {
                expert.AvailabilityEntries.Add(new AvailabilityEntry
                {
                    Id = Guid.NewGuid(),
                    ExpertId = expert.Id,
                    EffectiveFrom = LongAgo,
                    CapacityPercent = 7, // the decoy: superseded, and must never be the answer
                });
                expert.AvailabilityEntries.Add(new AvailabilityEntry
                {
                    Id = Guid.NewGuid(),
                    ExpertId = expert.Id,
                    EffectiveFrom = LongAgo.AddDays(1),
                    CapacityPercent = capacity,
                });
            }
            db.Experts.Add(expert);
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RosterPage> GetAsync(string query = "") =>
        await (await _client.GetAsync($"/api/experts/roster?pageSize=100&q={_marker}{query}"))
            .ReadOkAsync<RosterPage>();

    private static int Count(IReadOnlyList<RosterFacetCount> facet, string value) =>
        facet.Single(f => f.Value == value).Count;

    private static string[] Names(RosterPage page) => page.Items.Select(i => i.LastName).ToArray();

    // ---- the filters -------------------------------------------------------------------------

    [Fact]
    public async Task Postgres_filters_on_status()
    {
        var page = await GetAsync("&statuses=Draft");

        Names(page).Should().BeEquivalentTo(["Liskov", "Hopper"]);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task Postgres_filters_on_location_and_leaves_out_the_row_that_has_none()
    {
        var page = await GetAsync("&locations=London&locations=Arlington");

        Names(page).Should().NotContain("Johnson").And.NotContain("Stroustrup");
        page.Total.Should().Be(5);
    }

    [Theory]
    [InlineData(RosterBand.Full, new[] { "Lovelace", "Hopper", "Stroustrup" })]
    [InlineData(RosterBand.Partial, new[] { "Liskov", "Knuth", "Johnson" })]
    [InlineData(RosterBand.None, new[] { "Turing" })]
    public async Task The_availability_band_is_a_correlated_subquery_Postgres_can_filter_on(
        string band, string[] expected)
    {
        var page = await GetAsync($"&band={band}");

        Names(page).Should().BeEquivalentTo(expected,
            "the stale 7% entry never wins, and a person with no schedule is Unavailable");
        page.Total.Should().Be(expected.Length);
    }

    [Fact]
    public async Task Filters_combine_and_the_total_is_of_what_is_left()
    {
        var page = await GetAsync("&statuses=Active&locations=London&band=full");

        Names(page).Should().Equal(["Lovelace"]);
        page.Total.Should().Be(1);
    }

    // ---- the counts --------------------------------------------------------------------------

    [Fact]
    public async Task Postgres_counts_every_group_over_the_seeded_set()
    {
        var facets = (await GetAsync()).Facets;

        Count(facets.Status, "Active").Should().Be(5);
        Count(facets.Status, "Draft").Should().Be(2);
        Count(facets.Band, RosterBand.Full).Should().Be(3);
        Count(facets.Band, RosterBand.Partial).Should().Be(3);
        Count(facets.Band, RosterBand.None).Should().Be(1);
        facets.Location.Should().Equal([
            new RosterFacetCount("London", 3),
            new RosterFacetCount("Arlington", 2),
            new RosterFacetCount(Comma, 1),
        ], "busiest first, and a blank location is not a place");
    }

    [Fact]
    public async Task Each_group_is_counted_without_its_own_filter()
    {
        var facets = (await GetAsync("&statuses=Active&locations=London")).Facets;

        // Status counts come from London alone; location counts from Active alone; the band counts
        // from both, which is every group but their own.
        Count(facets.Status, "Active").Should().Be(2);
        Count(facets.Status, "Draft").Should().Be(1);
        Count(facets.Location, "London").Should().Be(2);
        Count(facets.Location, "Arlington").Should().Be(1);
        Count(facets.Location, Comma).Should().Be(1);
        Count(facets.Band, RosterBand.Full).Should().Be(1);
        Count(facets.Band, RosterBand.None).Should().Be(1);
        Count(facets.Band, RosterBand.Partial).Should().Be(0);
    }

    [Fact]
    public async Task A_location_narrowed_to_zero_keeps_its_row()
    {
        var facets = (await GetAsync($"&band={RosterBand.None}")).Facets;

        Count(facets.Location, "London").Should().Be(1, "only Turing is Unavailable");
        Count(facets.Location, "Arlington").Should().Be(0,
            "and Arlington stays on screen to be greyed out, not dropped");
    }

    /// <summary>
    /// The reason the filters ride as repeated keys. Joined into one comma-separated value and
    /// split apart again, "Cambridge, MA" would arrive as two places nobody is in and the roster
    /// would come back empty — a wrong answer with nothing on screen to say so.
    /// </summary>
    [Fact]
    public async Task A_location_with_a_comma_in_it_is_one_place_and_not_two()
    {
        var page = await GetAsync($"&locations={Uri.EscapeDataString(Comma)}");

        Names(page).Should().Equal(["Stroustrup"]);
        page.Total.Should().Be(1);
    }

    /// <summary>
    /// The one thing InMemory cannot show. A <c>GROUP BY</c> and a handful of <c>COUNT</c>s in the
    /// log say the numbers are the database's work; a service that materialised the match and
    /// counted the list in memory would return the same numbers and none of this SQL.
    /// </summary>
    [Fact]
    public async Task The_database_does_the_counting()
    {
        factory.Sql.Clear();

        await GetAsync("&statuses=Active");

        var sent = factory.Sql.Commands.Select(c => c.ToLowerInvariant()).ToList();
        sent.Should().Contain(c => c.Contains("group by"),
            "the status and location counts are a grouping Postgres did");
        sent.Count(c => c.Contains("count(")).Should().BeGreaterThan(1,
            "and the three availability bands are counted predicates, not a list we walked");
    }

    // ---- the 400s ----------------------------------------------------------------------------

    [Theory]
    [InlineData("&statuses=Paused")]
    [InlineData("&statuses=Active&statuses=Archived")]
    [InlineData("&statuses=2")]
    [InlineData("&band=mostly")]
    public async Task A_filter_the_roster_cannot_answer_is_a_400(string query)
    {
        var response = await _client.GetAsync($"/api/experts/roster?q={_marker}{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the rule is the Application layer's, so REST and MCP would refuse this identically");
    }

    [Fact]
    public async Task A_location_nobody_is_in_is_an_empty_roster_rather_than_a_400()
    {
        // Locations are free text off the roster itself, so there is no closed set to validate
        // against — a bookmark naming a city the last person left has to still render.
        var page = await GetAsync("&locations=Atlantis");

        page.Total.Should().Be(0);
        Count(page.Facets.Location, "Atlantis").Should().Be(0,
            "and the ticked box is still there to untick");
    }
}
