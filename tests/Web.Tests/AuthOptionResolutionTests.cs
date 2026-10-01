using ExpertToJob.Application.Auth;
using ExpertToJob.Web.Auth;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// What the running Web host actually resolves for <c>Auth</c> (EXP-89). Until this ticket most of
/// these values were spelled twice — once as a property default and once as a line in
/// <c>appsettings.json</c> holding the identical value — and a reader had no way to tell which copy
/// was load-bearing. Each now lives on exactly one side, which means the shipped file no longer
/// proves anything about them: the evidence has to come from the host.
///
/// <para>Asserted as literals, never against the property defaults they came from. Those two
/// agreeing is exactly what would hide a value that stopped resolving — the host would start, the
/// option would fall back to something nobody chose, and nothing would fail.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class AuthOptionResolutionTests(WebApiFactory factory)
{
    private AuthOptions Resolved =>
        factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

    /// <summary>The session identity, from the shared constants rather than from two JSON lines.
    /// <c>Agents.Tests/WebSessionTokenLockstepTests</c> holds the other host to the same pair.</summary>
    [Fact]
    public void The_host_resolves_the_shared_session_identity()
    {
        using var _ = new AssertionScope();
        Resolved.Jwt.Issuer.Should().Be("experttojob");
        Resolved.Jwt.Audience.Should().Be("experttojob-app");
        SessionIdentity.Issuer.Should().Be("experttojob");
        SessionIdentity.Audience.Should().Be("experttojob-app");
    }

    /// <summary>Session lifetime. Short on purpose — <c>User.TokenVersion</c> is the revocation
    /// mechanism and this is the window a revoked session could still slip through — so it is a
    /// property of the design and not an environment knob.</summary>
    [Fact]
    public void The_host_resolves_the_short_session_lifetime()
        => Resolved.Jwt.AccessTokenMinutes.Should().Be(15);

    /// <summary>The relying party, split by who owns each half: the name is the product's, the same
    /// everywhere, so it is a code default; the domain and the origins differ per deployment, so
    /// they are configured and have no code default to fall back to.</summary>
    [Fact]
    public void The_host_resolves_the_relying_party_from_both_sides()
    {
        using var _ = new AssertionScope();
        Resolved.Passkey.ServerName.Should().Be("ExpertToJob");
        Resolved.Passkey.ChallengeTimeoutSeconds.Should().Be(300);
        Resolved.Passkey.ServerDomain.Should().Be("localhost");
        Resolved.Passkey.Origins.Should().Contain("http://localhost:5173");
    }

    /// <summary>The per-deployment half really does come from configuration and nowhere else: an
    /// unconfigured relying-party id is empty, not a quiet <c>"localhost"</c>. A production host
    /// that bound every passkey to a domain nobody chose would never fail a request over it, and a
    /// credential registered under one RP id is unusable under another.</summary>
    [Fact]
    public void An_unconfigured_relying_party_domain_has_no_default()
        => new PasskeyOptions().ServerDomain.Should().BeEmpty();
}
