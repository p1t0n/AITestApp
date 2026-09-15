using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Users;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// <c>PUT /api/users/{id}/role</c> (P1T-238). Its own endpoint rather than a field on the user
/// update, so a privilege can never move as a side effect of editing an email or a token cap — the
/// reasoning is on P1T-229.
///
/// <para>The rules themselves are unit-tested against an isolated database in
/// <c>Application.Tests/UserServiceTests</c>; what is in scope here is the wire: the route, the
/// status codes, the sentence a refusal carries, and who is allowed to call it at all. The
/// last-Administrator rule is deliberately not asserted here — this suite shares one database with
/// every other Web test, so "how many Administrators exist" is not a fact a single test owns.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RoleChangeEndpointTests(WebApiFactory factory)
{
    private static string Route(Guid id) => $"/api/users/{id}/role";

    private int TokenVersionOf(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Users.AsNoTracking().Single(u => u.Id == id).TokenVersion;
    }

    [Fact]
    public async Task Promoting_a_user_returns_the_updated_row_and_revokes_its_sessions()
    {
        var client = factory.CreateAuthenticatedClient();
        var subject = factory.CreateAccount(UserRole.User);

        var response = await client.PutAsJsonAsync(
            Route(subject.Id), new ChangeRoleDto(UserRole.Administrator), WebApiFactory.Json);

        var body = await response.ReadOkAsync<UserDetailDto>();
        body.Role.Should().Be(UserRole.Administrator);
        TokenVersionOf(subject.Id).Should().Be(subject.TokenVersion + 1);

        // And the session minted before the change is no longer current — the token still claims
        // User, and the role travels in the token.
        var stale = factory.ClientForAccount(subject);
        (await stale.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Re_selecting_the_role_somebody_already_has_does_not_sign_them_out()
    {
        var client = factory.CreateAuthenticatedClient();
        var subject = factory.CreateAccount(UserRole.User);

        var response = await client.PutAsJsonAsync(
            Route(subject.Id), new ChangeRoleDto(UserRole.User), WebApiFactory.Json);

        (await response.ReadOkAsync<UserDetailDto>()).Role.Should().Be(UserRole.User);
        TokenVersionOf(subject.Id).Should().Be(subject.TokenVersion);
    }

    [Fact]
    public async Task Changing_your_own_role_is_a_409_carrying_the_sentence()
    {
        var (client, actor) = factory.CreateClientFor(UserRole.Administrator);

        var response = await client.PutAsJsonAsync(
            Route(actor.Id), new ChangeRoleDto(UserRole.User), WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadAsync<ProblemDetails>();
        problem.Detail.Should().Be("You cannot change your own role.");
        TokenVersionOf(actor.Id).Should().Be(actor.TokenVersion);
    }

    [Fact]
    public async Task An_unknown_id_is_a_404()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            Route(Guid.NewGuid()), new ChangeRoleDto(UserRole.Administrator), WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Staff-only, inherited from the controller — a User promoting themselves would make the whole
    /// role split decorative. <c>EndpointClassificationTests</c> proves the endpoint declares an
    /// audience at all; this proves which one it got.
    /// </summary>
    [Fact]
    public async Task A_user_cannot_change_anybody_s_role()
    {
        var client = factory.CreateUserClient();
        var subject = factory.CreateAccount(UserRole.User);

        var response = await client.PutAsJsonAsync(
            Route(subject.Id), new ChangeRoleDto(UserRole.Administrator), WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        TokenVersionOf(subject.Id).Should().Be(subject.TokenVersion);
    }

    [Fact]
    public async Task The_list_and_the_detail_carry_the_role()
    {
        var client = factory.CreateAuthenticatedClient();
        var subject = factory.CreateAccount(UserRole.User);

        var listed = await (await client.GetAsync("/api/users")).ReadOkAsync<List<UserSummaryDto>>();
        var detail = await (await client.GetAsync($"/api/users/{subject.Id}")).ReadOkAsync<UserDetailDto>();

        listed.Single(u => u.Id == subject.Id).Role.Should().Be(UserRole.User);
        detail.Role.Should().Be(UserRole.User);
    }
}
