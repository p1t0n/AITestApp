using System.Net;
using ExpertToJob.Application.Auth;
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

    /// <summary>
    /// Erasure, from the second host's point of view (P1T-186). The Web API is where somebody
    /// deletes themselves, and the agent surface is where their token would otherwise keep working
    /// for the rest of its lifetime — while we have already told them their data is gone. Nothing
    /// special makes this work: the account is simply not there any more, and both hosts re-read it.
    /// </summary>
    [Fact]
    public async Task An_erased_account_takes_its_agent_session_with_it()
    {
        using var factory = AgentsHost();
        var userId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient(userId);

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the session works before");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.RemoveRange(db.Users.Where(u => u.Id == userId));
            await db.SaveChangesAsync();
        }

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "and not after");
    }

    [Fact]
    public async Task A_token_naming_no_account_is_refused()
    {
        using var factory = AgentsHost();

        // Signature, issuer, audience and lifetime all valid; there is simply nobody behind it.
        using var client = factory.CreateClientWithClaims(
            Guid.NewGuid(), nameof(UserRole.Administrator), tokenVersion: 1);

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
            userId, nameof(UserRole.Administrator), tokenVersion: null);

        (await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Default-deny on the agent surface too. Every agent endpoint declares a bare
    /// <c>RequireAuthorization()</c>, and the host's default policy is Administrator — so an
    /// Expert's session, valid in every other respect, reaches none of them.
    /// </summary>
    [Fact]
    public async Task An_expert_token_is_refused_on_the_agent_surface()
    {
        using var factory = AgentsHost();
        var userId = Guid.NewGuid();
        factory.EnsureAccount(userId, UserRole.User);
        using var client = factory.CreateClientForRole(userId, UserRole.User);

        var response = await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// "Who is this request from" is answered in exactly one place, and it is the same place the
    /// revocation check above reads: <see cref="SessionRevocation.UserId"/> in the Application
    /// layer. The Agents host carried a second copy of that parse (<c>Agents.Usage.UserClaims</c>,
    /// EXP-88) — same two claim types, same <c>Guid.TryParse</c>, nineteen call sites — which is
    /// the shape of duplication that costs nothing until the day one copy learns about a third
    /// claim and the other does not. Then the surface this file guards accepts a principal the
    /// revocation check cannot find an account for, or the reverse.
    ///
    /// <para>Swept over the host's own sources rather than asserted by reflection: a private
    /// re-parse is just as capable of drifting as a public one, and only the source shows it.</para>
    /// </summary>
    [Fact]
    public void The_agents_host_keeps_no_second_reader_of_the_session_subject()
    {
        var root = RepoRoot();
        var agents = Path.Combine(root, "api", "Agents");
        var separator = Path.DirectorySeparatorChar;

        var offenders = Directory
            .EnumerateFiles(agents, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                           && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path)
                .Contains("ClaimTypes.NameIdentifier", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(separator, '/'))
            .ToList();

        offenders.Should().BeEmpty(
            "the subject claim is read through SessionRevocation.UserId, which both hosts share; "
            + "found a local parse in: " + string.Join(", ", offenders));
    }

    /// <summary>Walks up from the test binary until the solution file appears.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the source sweep cannot run.");
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
