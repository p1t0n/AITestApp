using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The bench listing's filters (EXP-94). Roster Q&amp;A answered "there are no experts located in
/// Warsaw" over a roster holding 31 of them, because <c>expert_list</c> could only return all 505
/// rows and the Tool Result Budget refused them. The filters are the fix, and they live here — in
/// the Application layer, the single behaviour seam — so REST and MCP narrow identically.
/// </summary>
public class ExpertListFilterTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"list-filter-{Guid.NewGuid()}")
            .Options);

    private static ExpertService NewService(AppDbContext db, IRosterAudienceProvider? audience = null) =>
        new(db, new SaveExpertValidator(), new UpdateExpertValidator(), new RosterQueryValidator(),
            new UnrestrictedOwnershipScopeProvider(), audience ?? new AdministrationAudienceProvider(),
            TimeProvider.System);

    private static SaveExpertDto Dto(string last, string? location, string email) =>
        new("Ada", last, "Senior Engineer", email, null, location, null, null);

    private static async Task<ExpertService> SeededAsync(AppDbContext db)
    {
        var svc = NewService(db);
        await svc.CreateAsync(Dto("Kowalski", "Warsaw, Poland", "k@example.com"));
        await svc.CreateAsync(Dto("Nowak", "warsaw, poland", "n@example.com"));
        await svc.CreateAsync(Dto("Schmidt", "Berlin, Germany", "s@example.com"));
        await svc.CreateAsync(Dto("Nowhere", null, "x@example.com"));
        return svc;
    }

    [Fact]
    public async Task A_location_filter_matches_a_case_insensitive_substring_and_counts_the_match()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var result = await svc.ListAsync(new ExpertListQuery(Location: "WARSAW"));

        result.Total.Should().Be(2);
        result.Items.Select(e => e.LastName).Should().BeEquivalentTo("Kowalski", "Nowak");
    }

    [Fact]
    public async Task An_expert_with_no_location_never_matches_a_location_filter()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var result = await svc.ListAsync(new ExpertListQuery(Location: "a"));

        result.Items.Should().NotContain(e => e.LastName == "Nowhere");
    }

    [Fact]
    public async Task No_filter_is_the_whole_bench_and_its_total()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var result = await svc.ListAsync(new ExpertListQuery());

        result.Total.Should().Be(4);
        result.Items.Should().HaveCount(4);
    }

    /// <summary>
    /// RosterVisibility is applied exactly as it was, and before the filters — so a paused expert
    /// in Warsaw is neither listed nor counted. A total that included them would leak the one fact
    /// the pause exists to withhold.
    /// </summary>
    [Fact]
    public async Task A_paused_expert_is_neither_listed_nor_counted_for_the_bench_audience()
    {
        await using var db = NewDb();
        var seeded = await SeededAsync(db);
        var paused = await db.Experts.SingleAsync(e => e.LastName == "Kowalski");
        paused.HiddenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var bench = NewService(db, new BenchAudienceProvider());
        var benchResult = await bench.ListAsync(new ExpertListQuery(Location: "warsaw"));

        benchResult.Total.Should().Be(1, "the paused Warsaw expert is not on the bench");
        benchResult.Items.Should().OnlyContain(e => e.LastName == "Nowak");

        // The administration audience still sees them — the pause hides a row from the bench, not
        // from the people accountable for it.
        (await seeded.ListAsync(new ExpertListQuery(Location: "warsaw"))).Total.Should().Be(2);
    }

    /// <summary>EXP-100 deleted the <c>ListAsync(bool)</c> overload — it had no production
    /// caller left once the controller and the tool both moved to the query. An unfiltered query
    /// is the same listing it used to return, in the same order.</summary>
    [Fact]
    public async Task An_empty_query_still_returns_the_same_rows_in_the_same_order()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var rows = (await svc.ListAsync(new ExpertListQuery())).Items;

        rows.Select(e => e.LastName).Should().Equal("Kowalski", "Nowak", "Nowhere", "Schmidt");
    }

    /// <summary>
    /// EXP-96: "how many experts are on the roster in total?" had no answer. The only way to ask
    /// was for every row, and over the 505-expert demo roster that is ~31k tokens the Tool Result
    /// Budget refuses — so the count-only mode returns the total and no rows at all.
    /// </summary>
    [Fact]
    public async Task Count_only_returns_the_total_and_no_rows()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var result = await svc.ListAsync(new ExpertListQuery(CountOnly: true));

        result.Total.Should().Be(4);
        result.Items.Should().BeEmpty();
    }

    /// <summary>Count-only counts the same match the rows would have been drawn from, so a
    /// filtered count is the filter's total and not the bench's.</summary>
    [Fact]
    public async Task Count_only_counts_the_filtered_match()
    {
        await using var db = NewDb();
        var svc = await SeededAsync(db);

        var result = await svc.ListAsync(new ExpertListQuery(Location: "WARSAW", CountOnly: true));

        result.Total.Should().Be(2);
        result.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Visibility is applied exactly as it is for row listing — before the filters, and therefore
    /// before the count. A hidden expert counted here would leak the one fact the pause withholds,
    /// and it would do it through the cheapest call on the surface.
    /// </summary>
    [Fact]
    public async Task Count_only_does_not_count_a_hidden_expert_for_the_bench_audience()
    {
        await using var db = NewDb();
        await SeededAsync(db);
        var paused = await db.Experts.SingleAsync(e => e.LastName == "Kowalski");
        paused.HiddenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var bench = NewService(db, new BenchAudienceProvider());

        (await bench.ListAsync(new ExpertListQuery(CountOnly: true))).Total.Should().Be(3);
        (await bench.ListAsync(new ExpertListQuery(Location: "warsaw", CountOnly: true)))
            .Total.Should().Be(1);
    }
}
