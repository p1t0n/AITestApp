using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ExpertToJob.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The OpenAPI document, over the real host (EXP-74). Nothing covered it while Swashbuckle
/// generated it, so the switch to ASP.NET Core 11's built-in generator had nothing to break
/// against — these are the assertions that make the generator swappable: the document is served in
/// Development and only there, it describes the controllers, and the UI CLAUDE.md promises at
/// <c>/swagger</c> still answers.
/// </summary>
[Collection(WebApiCollection.Name)]
public class OpenApiDocumentTests(WebApiFactory factory)
{
    /// <summary>A route that has been in the API since the first controller and is not going
    /// anywhere, so the assertion is about the document rather than about this week's endpoints.</summary>
    private const string StableRoute = "/api/experts";

    [Fact]
    public async Task Development_serves_the_document()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task The_document_describes_the_controllers()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty(StableRoute, out var experts).Should()
            .BeTrue($"the generator must describe the controller route {StableRoute}");
        experts.TryGetProperty("get", out _).Should().BeTrue();
    }

    /// <summary>
    /// The document is a development affordance, not a public surface: a Production host must not
    /// publish its route table. Only the environment and the signing key move here — a Production
    /// host refuses to boot on the dev placeholder (P1T-87), so the key is the price of asking the
    /// question at all.
    ///
    /// <para>The client is an Administrator on purpose. The app-wide fallback policy answers 401 for
    /// a request that matches no endpoint, so an anonymous 401 would prove nothing about whether the
    /// document is there; signed in as the most privileged role, 404 is the route table talking.</para>
    /// </summary>
    [Fact]
    public async Task Production_does_not_serve_the_document()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:SigningKey", "not-a-placeholder-key-for-the-production-boot-guard");
        });

        var account = factory.CreateAccount(UserRole.Administrator);
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", WebApiFactory.SessionTokenFor(production.Services, account));

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CLAUDE.md and the AppHost table both promise "Swagger at <c>/swagger</c>", so the UI
    /// stays at that address whatever generates the document behind it.</summary>
    [Fact]
    public async Task The_swagger_ui_answers_at_its_promised_address()
    {
        using var client = factory.CreateDefaultClient(new RedirectHandler());

        var response = await client.GetAsync("/swagger");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
