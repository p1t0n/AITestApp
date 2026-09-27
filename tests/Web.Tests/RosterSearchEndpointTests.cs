using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// <c>GET /api/experts/roster</c> against real Postgres (EXP-45).
///
/// <para>The rules — which rows match, in what order, on which page — are settled over EF InMemory
/// in <c>Application.Tests/RosterSearchTests</c>, which is fast and needs no container. What only
/// Postgres can answer is here: that the correlated subquery behind "availability today" and the
/// null-tolerant location order translate at all, and that the paging and the count are the
/// database's work rather than a materialised list's.</para>
///
/// <para>The suite shares one database, so every row this class seeds carries a marker in its title
/// and every request searches for that marker. The assertions are then about a set this class owns,
/// not about a collection total another class can move.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RosterSearchEndpointTests(WebApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    /// <summary>The marker, unique per run: a nonsense word no other test's title contains.</summary>
    private readonly string _marker = $"zz{Guid.NewGuid():N}";

    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    /// <summary>
    /// Seven people whose sortable fields disagree with each other, so no ordering assertion can
    /// pass on the insertion order. One has no location, one is a Draft, one paused themselves, and
    /// the availability schedules each carry a stale entry the step function must not pick.
    /// </summary>
    private readonly (string First, string Last, string? Location, int Capacity, ExpertStatus Status, bool Paused)[]
        _people =
        [
            ("Ada", "Lovelace", "London", 50, ExpertStatus.Active, false),
            ("Grace", "Hopper", "Arlington", 100, ExpertStatus.Draft, false),
            ("Alan", "Turing", "Wilmslow", 0, ExpertStatus.Active, false),
            ("Katherine", "Johnson", null, 80, ExpertStatus.Active, false),
            ("Edsger", "Dijkstra", "Rotterdam", 20, ExpertStatus.Active, true),
            ("Barbara", "Liskov", "Cambridge", 60, ExpertStatus.Active, false),
            ("Donald", "Knuth", "Stanford", 40, ExpertStatus.Active, false),
        ];

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (first, last, location, capacity, status, paused) in _people)
        {
            var expert = new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = first,
                LastName = last,
                // The marker lives in the title, which is one of the three fields search covers —
                // so the same string that isolates the set also exercises the title match.
                Title = $"{_marker} Engineer",
                Location = location,
                Email = ApiClientExtensions.UniqueEmail(last.ToLowerInvariant()),
                Status = status,
                HiddenAt = paused ? DateTimeOffset.UtcNow : null,
            };
            // Seeded through the DbContext rather than through POST, because the fields this class
            // sorts on — a null location, a pause, an availability schedule — are not all reachable
            // from the create endpoint. The lawful-basis record is not optional even so: these rows
            // join the shared database, and `ProcessingRecordDatabaseTests` audits every Expert in
            // it. A roster row with no recorded basis is a compliance defect wherever it came from.
            expert.ProcessingRecords.Add(ProcessingRecord.For(
                expert.Id, sequence: 1, ProcessingOrigin.StaffCreated,
                noticeVersion: null, "Seeded by RosterSearchEndpointTests.", DateTimeOffset.UtcNow));
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
            db.Experts.Add(expert);
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RosterPage> GetAsync(string query = "") =>
        await (await _client.GetAsync($"/api/experts/roster?q={_marker}{query}"))
            .ReadOkAsync<RosterPage>();

    [Fact]
    public async Task The_whole_seeded_set_comes_back_Draft_and_paused_included()
    {
        var page = await GetAsync("&pageSize=100");

        page.Total.Should().Be(7);
        page.Items.Should().Contain(i => i.Status == ExpertStatus.Draft, "the Roster holds Drafts");
        page.Items.Should().Contain(i => i.HiddenAt != null, "and people who paused themselves");
    }

    [Fact]
    public async Task Postgres_pages_the_set_without_repeating_or_losing_a_row()
    {
        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await GetAsync($"&pageSize=3&page={page}");

            slice.Total.Should().Be(7, "every page reports the size of the whole match");
            slice.Items.Should().HaveCount(page == 3 ? 1 : 3, "the last page is the remainder");
            seen.AddRange(slice.Items.Select(i => i.Id));
        }

        seen.Should().OnlyHaveUniqueItems().And.HaveCount(7);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_and_still_counts_the_match()
    {
        var page = await GetAsync("&pageSize=3&page=9");

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(7);
    }

    /// <summary>
    /// The one thing InMemory cannot show. `LIMIT`/`OFFSET` and a `count` of their own say the
    /// slice and the total were the database's work — a service that materialised the match and
    /// took 3 from the list would return the same three rows and none of this SQL.
    /// </summary>
    [Fact]
    public async Task The_database_does_the_paging_and_the_counting()
    {
        factory.Sql.Clear();

        await GetAsync("&pageSize=3&page=2");

        var sent = factory.Sql.Commands.Select(c => c.ToLowerInvariant()).ToList();
        sent.Should().Contain(c => c.Contains("count("),
            "the total is a COUNT over the match, not the length of a list we already paid for");
        sent.Should().Contain(c => c.Contains("limit") && c.Contains("offset"),
            "and the page is a slice the database took");
    }

    [Fact]
    public async Task Availability_today_is_a_correlated_subquery_Postgres_can_order_by()
    {
        var most = await GetAsync("&sort=capacity&dir=desc&pageSize=100");
        var least = await GetAsync("&sort=capacity&dir=asc&pageSize=100");

        most.Items.Select(i => i.CurrentCapacityPercent).Should()
            .Equal([100, 80, 60, 50, 40, 20, 0], "the stale 7% entry never wins");
        least.Items.Select(i => i.CurrentCapacityPercent).Should()
            .Equal(most.Items.Select(i => i.CurrentCapacityPercent).Reverse());
    }

    [Fact]
    public async Task Ordering_by_location_survives_a_row_that_has_none()
    {
        var asc = await GetAsync("&sort=location&dir=asc&pageSize=100");

        // Katherine Johnson has no location. It sorts as the empty string — our decision — rather
        // than landing wherever Postgres puts NULL, which is last ascending and first descending.
        asc.Items.Select(i => i.Location).Should()
            .Equal([null, "Arlington", "Cambridge", "London", "Rotterdam", "Stanford", "Wilmslow"]);
    }

    [Fact]
    public async Task Name_is_the_default_order_and_reverses()
    {
        var byDefault = await GetAsync("&pageSize=100");
        var desc = await GetAsync("&sort=name&dir=desc&pageSize=100");

        byDefault.Items.Select(i => i.LastName).Should()
            .Equal(["Dijkstra", "Hopper", "Johnson", "Knuth", "Liskov", "Lovelace", "Turing"]);
        desc.Items.Select(i => i.LastName).Should()
            .Equal(byDefault.Items.Select(i => i.LastName).Reverse());
    }

    [Fact]
    public async Task Search_narrows_the_set_and_the_total_with_it()
    {
        var page = await GetAsync("&q=" + _marker); // the marker twice is still just the marker
        page.Total.Should().Be(7);

        var narrowed = await (await _client.GetAsync($"/api/experts/roster?q=lovelace&pageSize=100"))
            .ReadOkAsync<RosterPage>();

        narrowed.Items.Should().Contain(i => i.LastName == "Lovelace");
        narrowed.Items.Should().NotContain(i => i.LastName == "Turing");
    }

    // ---- the 400s ----------------------------------------------------------------------------

    [Theory]
    [InlineData("?sort=firstname")]
    [InlineData("?dir=sideways")]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task A_query_the_roster_cannot_answer_is_a_400(string query)
    {
        var response = await _client.GetAsync($"/api/experts/roster{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the rule is the Application layer's, so REST and MCP would refuse this identically");
    }

    [Fact]
    public async Task The_largest_page_the_cap_allows_is_not_a_400()
    {
        var response = await _client.GetAsync("/api/experts/roster?pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "101 is the first refusal, not 100");
    }

    [Fact]
    public async Task The_roster_page_is_staff_only()
    {
        var response = await factory.CreateUserClient().GetAsync("/api/experts/roster");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "this is the one query that would hand over the whole product");
    }

    /// <summary>
    /// The bench list the MCP tools and the agent pickers read is a different endpoint with a
    /// different shape, and this slice did not touch it.
    /// </summary>
    [Fact]
    public async Task The_bench_list_is_unchanged_and_still_a_bare_array_of_Active_experts()
    {
        var bench = await _client.GetFromJsonAsync<List<ExpertSummaryDto>>(
            "/api/experts", WebApiFactory.Json);

        bench.Should().NotBeNull();
        bench!.Should().NotContain(e => e.Status == ExpertStatus.Draft);
        bench.Should().Contain(e => e.Title.StartsWith(_marker), "our Active rows are on the bench");
    }
}
