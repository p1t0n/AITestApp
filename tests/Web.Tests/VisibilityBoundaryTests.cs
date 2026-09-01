using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Visibility;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The pause control at the HTTP boundary (P1T-185). Two things can only be shown here: that the
/// API has no way to express "pause somebody else" — there is no id in any of these URLs — and that
/// the Web host is the administration audience, so a Service Manager keeps seeing a paused Expert,
/// marked, instead of watching the bench silently lose somebody.
/// </summary>
[Collection(WebApiCollection.Name)]
public class VisibilityBoundaryTests(WebApiFactory factory)
{
    [Fact]
    public async Task An_expert_pauses_and_resumes_their_own_row()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, _) = factory.CreateExpertClientOwning(row.Id);
        using var _client = client;

        (await (await client.GetAsync("/api/me/visibility")).ReadOkAsync<ExpertVisibilityDto>())
            .Hidden.Should().BeFalse();

        var paused = await (await client.PostAsJsonAsync("/api/me/visibility/hide", new { }))
            .ReadOkAsync<ExpertVisibilityDto>();

        paused.Hidden.Should().BeTrue();
        paused.HiddenSince.Should().NotBeNull("the transparency view has to be able to say since when");
        paused.ExpertId.Should().Be(row.Id);
        HiddenAtOf(row.Id).Should().NotBeNull();

        var resumed = await (await client.PostAsJsonAsync("/api/me/visibility/unhide", new { }))
            .ReadOkAsync<ExpertVisibilityDto>();

        resumed.Hidden.Should().BeFalse();
        resumed.HiddenSince.Should().BeNull();
        HiddenAtOf(row.Id).Should().BeNull();
    }

    /// <summary>
    /// Pausing twice keeps the first timestamp. "Since when" is a fact about the pause, not about
    /// the click — a second press must not quietly restart the clock the transparency view reads.
    /// </summary>
    [Fact]
    public async Task Pausing_twice_does_not_move_the_timestamp()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, _) = factory.CreateExpertClientOwning(row.Id);
        using var _client = client;

        var first = await (await client.PostAsJsonAsync("/api/me/visibility/hide", new { }))
            .ReadOkAsync<ExpertVisibilityDto>();
        var second = await (await client.PostAsJsonAsync("/api/me/visibility/hide", new { }))
            .ReadOkAsync<ExpertVisibilityDto>();

        second.HiddenSince.Should().Be(first.HiddenSince);
    }

    /// <summary>
    /// The rule that hiding is the Expert's alone, checked the only way it can be checked: there is
    /// no route that names another row. A Service Manager who wants somebody off the bench
    /// deactivates the account, which is a different mechanism with a different meaning — and staff
    /// cannot un-hide somebody who hid themselves.
    /// </summary>
    [Fact]
    public async Task No_route_exists_for_pausing_somebody_else()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());

        foreach (var path in new[]
                 {
                     $"/api/me/visibility/hide/{row.Id}",
                     $"/api/experts/{row.Id}/hide",
                     $"/api/experts/{row.Id}/visibility",
                 })
        {
            (await staff.PostAsJsonAsync(path, new { })).StatusCode
                .Should().Be(HttpStatusCode.NotFound, $"{path} must not exist");
        }

        // And a Service Manager who owns no row of their own gets the ordinary "you have no row"
        // answer from the only route there is, rather than a way in.
        (await staff.PostAsJsonAsync("/api/me/visibility/hide", new { })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A Service Manager who is also on the bench pauses their own row like anybody else —
    /// ownership is independent of role, and this is the one case where staff legitimately press
    /// this button.
    /// </summary>
    [Fact]
    public async Task A_service_manager_who_is_on_the_bench_pauses_their_own_row()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, account) = factory.CreateClientFor(UserRole.ServiceManager);
        using var _client = client;
        factory.SetOwner(row.Id, account.Id);

        var paused = await (await client.PostAsJsonAsync("/api/me/visibility/hide", new { }))
            .ReadOkAsync<ExpertVisibilityDto>();

        paused.Hidden.Should().BeTrue();
    }

    // ---- What staff still see ---------------------------------------------------------------------

    /// <summary>
    /// The Web host is the administration audience, so a paused Expert stays on the roster and is
    /// marked. A bench that silently loses somebody is a bench nobody can explain — and the badge is
    /// how staff tell a paused person from one who never existed.
    /// </summary>
    [Fact]
    public async Task A_paused_expert_stays_on_the_staff_roster_and_is_marked()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        SetHidden(row.Id, DateTimeOffset.UtcNow);

        var roster = await (await staff.GetAsync("/api/experts")).ReadOkAsync<List<ExpertSummaryDto>>();
        var listed = roster.Single(e => e.Id == row.Id);

        listed.HiddenAt.Should().NotBeNull("staff see the pause, they do not lose the person");

        var detail = await (await staff.GetAsync($"/api/experts/{row.Id}")).ReadOkAsync<ExpertDetailDto>();
        detail.HiddenAt.Should().NotBeNull();
    }

    /// <summary>
    /// And the owner still reaches their own paused row — they are the one who paused it, so
    /// filtering it away from them would lock them out of the control that undoes it.
    /// </summary>
    [Fact]
    public async Task The_owner_still_reaches_their_own_paused_row()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, _) = factory.CreateExpertClientOwning(row.Id);
        using var _client = client;

        await client.PostAsJsonAsync("/api/me/visibility/hide", new { });

        var mine = await (await client.GetAsync($"/api/experts/{row.Id}")).ReadOkAsync<ExpertDetailDto>();
        mine.HiddenAt.Should().NotBeNull();

        (await client.GetAsync("/api/me/visibility")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Staff keep full write on CV content while somebody is paused — the pause is an exit control,
    /// not a content lock, and conflating the two would quietly make a paused record unmaintainable.
    /// </summary>
    [Fact]
    public async Task Staff_can_still_edit_a_paused_experts_record()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        SetHidden(row.Id, DateTimeOffset.UtcNow);

        var response = await staff.PatchAsJsonAsync(
            $"/api/experts/{row.Id}", new { title = "Principal Engineer" }, WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadAsync<ExpertDetailDto>()).Title.Should().Be("Principal Engineer");
    }

    private void SetHidden(Guid expertId, DateTimeOffset? at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Experts.Single(e => e.Id == expertId).HiddenAt = at;
        db.SaveChanges();
    }

    private DateTimeOffset? HiddenAtOf(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Experts.AsNoTracking().Single(e => e.Id == expertId).HiddenAt;
    }
}
