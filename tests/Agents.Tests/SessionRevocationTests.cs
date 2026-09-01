using System.Net;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Revocation on the second host (P1T-181). The Agents service validates the Web host's token from
/// its own configuration, so "this session is over" has to be a fact it re-reads rather than
/// something the Web API remembers. Without the token-version check here, signing someone out — or
/// erasing them — would close the REST API and leave the whole agent surface open to their token
/// until it expired.
///
/// <para>Same shape as <c>Web.Tests/AuthBoundaryTests</c> deliberately: the two hosts are held to
/// one rule, and the rule itself lives in <c>Application.Auth.SessionRevocation</c>.</para>
/// </summary>
public class SessionRevocationTests
{
    /// <summary>Authorizes, then does nothing expensive: 404 authenticated, 401 not.</summary>
    private const string AuthorizedProbe = "/agents/staffing/proposals/";

    private static WebApplicationFactory<Program> AgentsHost()
    {
        // The name is computed once, outside the options callback: that callback runs per DbContext
        // instance, so building the name inside it would hand every scope its own empty database.
        var dbName = $"revocation-{Guid.NewGuid()}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
                    .IDbContextOptionsConfiguration<AppDbContext>));
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            }));
    }

    [Fact]
    public async Task A_superseded_token_version_refuses_a_previously_valid_token()
    {
        using var factory = AgentsHost();
        var userId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient(userId);

        // Accepted first, so the refusal below cannot be blamed on the token or the probe.
        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        factory.RevokeSessions(userId);

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_naming_no_account_is_refused()
    {
        using var factory = AgentsHost();

        // Signature, issuer, audience and lifetime all valid; there is simply nobody behind it.
        using var client = factory.CreateClientWithClaims(
            Guid.NewGuid(), nameof(UserRole.ServiceManager), tokenVersion: 1);

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A token with no version claim cannot be checked for revocation, so it is not accepted —
    /// otherwise omitting the claim would be a way to opt out of revocation altogether.
    /// </summary>
    [Fact]
    public async Task A_token_without_a_version_claim_is_refused()
    {
        using var factory = AgentsHost();
        var userId = Guid.NewGuid();
        factory.EnsureAccount(userId);

        using var client = factory.CreateClientWithClaims(
            userId, nameof(UserRole.ServiceManager), tokenVersion: null);

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Default-deny on the agent surface too. Every agent endpoint declares a bare
    /// <c>RequireAuthorization()</c>, and the host's default policy is ServiceManager — so an
    /// Expert's session, valid in every other respect, reaches none of them.
    /// </summary>
    [Fact]
    public async Task An_expert_token_is_refused_on_the_agent_surface()
    {
        using var factory = AgentsHost();
        var userId = Guid.NewGuid();
        factory.EnsureAccount(userId, UserRole.Expert);
        using var client = factory.CreateClientForRole(userId, UserRole.Expert);

        var response = await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    /// <summary>Liveness stays anonymous: an orchestrator has no session token.</summary>
    [Fact]
    public async Task Health_stays_anonymous()
    {
        using var factory = AgentsHost();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
