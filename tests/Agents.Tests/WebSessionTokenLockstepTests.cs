using System.Net;
using ExpertToJob.Application.Auth;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The session-token lockstep (P1T-176). The Web host mints the session JWT; the Agents host is a
/// separate process that validates it from its own copy of <c>Auth:Jwt</c>. Nothing at build time
/// ties the two copies together, so an identity rename that lands in one <c>appsettings.json</c>
/// and not the other silently 401s every agent call — the app still starts, still serves the SPA,
/// and only the agent surface goes dark.
///
/// <para>Since EXP-106 the drift is closed by construction: neither host reads the names from
/// configuration at all, so there is only one copy and it lives in <see cref="SessionIdentity"/>,
/// in the layer both hosts reference. What is left to catch is the copy coming <em>back</em> — a
/// <c>Auth:Jwt:Issuer</c> line re-added to either host's settings is now inert, and an operator
/// reading it would believe they had changed the session identity when they had not. So both
/// shipped configurations are asserted to carry no such key.</para>
///
/// <para>Two copies of one fact on purpose: the shared constant, and the names pinned below. The
/// pin is what stops the constant drifting into a name nobody chose.</para>
///
/// <para>Deterministic: the Web side is the shipped config on disk (copied beside the test binary),
/// and the Agents side is the real host's own configuration and its real JWT middleware.</para>
/// </summary>
public class WebSessionTokenLockstepTests
{
    // Pinned as literals, never as SessionIdentity.Issuer: the constant and the hosts agreeing is
    // exactly what would hide a rename that moved all three together.
    private const string ExpectedIssuer = "experttojob";
    private const string ExpectedAudience = "experttojob-app";

    /// <summary>An endpoint that authorizes and then does nothing expensive: authenticated it is a
    /// 404 for an unknown id, unauthenticated it is a 401. The difference is the whole assertion.</summary>
    private const string AuthorizedProbe = "/agents/staffing/proposals/";

    private static readonly IConfigurationRoot WebSettings = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json"))
        .Build();

    private static WebApplicationFactory<Program> AgentsHost()
    {
        // Named once, outside the callback: it runs per DbContext instance, and a name built inside
        // it would give each scope its own database — so the account a token names would vanish
        // between minting the token and validating it (P1T-181's revocation check reads that row).
        var dbName = $"lockstep-{Guid.NewGuid()}";
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
    public void The_two_hosts_share_one_session_identity_and_neither_ships_a_copy()
    {
        using var factory = AgentsHost();
        var agents = factory.Services.GetRequiredService<IConfiguration>();

        using var _ = new AssertionScope();
        SessionIdentity.Issuer.Should().Be(ExpectedIssuer);
        SessionIdentity.Audience.Should().Be(ExpectedAudience);

        // A re-added JSON line would not drift the two hosts any more — it would be read by
        // neither, which is its own way of lying to whoever set it.
        WebSettings["Auth:Jwt:Issuer"].Should().BeNull("the Web host mints from the shared constant");
        WebSettings["Auth:Jwt:Audience"].Should().BeNull("the Web host mints from the shared constant");
        agents["Auth:Jwt:Issuer"].Should().BeNull("the Agents host validates against the shared constant");
        agents["Auth:Jwt:Audience"].Should().BeNull("the Agents host validates against the shared constant");
    }

    [Fact]
    public async Task A_web_minted_session_token_is_accepted_by_the_agents_service()
    {
        using var factory = AgentsHost();
        using var client = factory.CreateClientWithToken(SessionIdentity.Issuer, SessionIdentity.Audience);

        var response = await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}");

        // 404, not 401: the token passed validation and the handler simply found no such proposal.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_from_a_foreign_issuer_is_still_rejected()
    {
        // Keeps the check above honest — it must fail for the right reason, not because the host
        // stopped validating issuers at all.
        using var factory = AgentsHost();
        using var client = factory.CreateClientWithToken("some-other-product", SessionIdentity.Audience);

        var response = await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
