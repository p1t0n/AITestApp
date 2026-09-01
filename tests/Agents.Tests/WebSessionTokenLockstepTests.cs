using System.Net;
using System.Text.Json;
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
/// <para>Hence three copies of one fact on purpose: the shipped Web config, the shipped Agents
/// config, and the names pinned below. Two of them drifting is what this catches, and the third is
/// what stops both drifting together into a name nobody chose.</para>
///
/// <para>Deterministic: the Web config is JSON on disk (copied beside the test binary), and the
/// Agents side is the real host's own configuration and its real JWT middleware.</para>
/// </summary>
public class WebSessionTokenLockstepTests
{
    private const string ExpectedIssuer = "experttojob";
    private const string ExpectedAudience = "experttojob-app";

    /// <summary>An endpoint that authorizes and then does nothing expensive: authenticated it is a
    /// 404 for an unknown id, unauthenticated it is a 401. The difference is the whole assertion.</summary>
    private const string AuthorizedProbe = "/agents/staffing/proposals/";

    private static readonly JsonDocument WebSettings = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json")));

    private static string WebJwt(string key) =>
        WebSettings.RootElement.GetProperty("Auth").GetProperty("Jwt").GetProperty(key).GetString()!;

    private static WebApplicationFactory<Program> AgentsHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure
                    .IDbContextOptionsConfiguration<AppDbContext>));
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"lockstep-{Guid.NewGuid()}"));
            }));

    [Fact]
    public void The_two_hosts_ship_the_same_session_identity()
    {
        using var factory = AgentsHost();
        var agents = factory.Services.GetRequiredService<IConfiguration>();

        using var _ = new AssertionScope();
        WebJwt("Issuer").Should().Be(ExpectedIssuer);
        WebJwt("Audience").Should().Be(ExpectedAudience);
        agents["Auth:Jwt:Issuer"].Should().Be(ExpectedIssuer, "the Agents host validates what Web mints");
        agents["Auth:Jwt:Audience"].Should().Be(ExpectedAudience, "the Agents host validates what Web mints");
    }

    [Fact]
    public async Task A_web_minted_session_token_is_accepted_by_the_agents_service()
    {
        using var factory = AgentsHost();
        using var client = factory.CreateClientWithToken(WebJwt("Issuer"), WebJwt("Audience"));

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
        using var client = factory.CreateClientWithToken("some-other-product", WebJwt("Audience"));

        var response = await client.GetAsync($"{AuthorizedProbe}{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
