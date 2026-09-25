using FluentAssertions;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The relying-party identity every passkey is bound to. A credential registered under one RP id
/// is unusable under another, so a drift here — a renamed Fido2 property that binds to nothing, a
/// default that changes under a package bump — locks every existing user out without a single
/// failing request. Fido2.AspNet 4.1 moved <c>ServerDomain</c>/<c>ServerName</c> to
/// <c>RPID</c>/<c>RPName</c>; this pins the values the host actually hands the library.
/// </summary>
[Collection(WebApiCollection.Name)]
public class PasskeyRelyingPartyTests(WebApiFactory factory)
{
    [Fact]
    public void Host_configures_the_relying_party_from_Auth_Passkey()
    {
        var fido = factory.Services.GetRequiredService<Fido2Configuration>();

        fido.RPID.Should().Be("localhost");
        fido.RPName.Should().Be("ExpertToJob");
        fido.Origins.Should().NotBeEmpty();
    }
}
