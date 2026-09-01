using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The service boundary itself. Authorization is declared per controller and backed by a
/// staff-only fallback policy, so only a real request proves what each rule does: a request without
/// a token, a request with an Expert's token, and a request with a token whose generation has been
/// superseded. These are the executable answer to "who can reach the Web API?".
/// </summary>
[Collection(WebApiCollection.Name)]
public class AuthBoundaryTests(WebApiFactory factory)
{
    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/experts" },
        { "GET", "/api/experts/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/experts/00000000-0000-0000-0000-000000000001/cv" },
        { "GET", "/api/experts/00000000-0000-0000-0000-000000000001/cv.pdf" },
        { "GET", "/api/catalog/categories" },
        { "GET", "/api/catalog/categories/tree" },
        { "GET", "/api/catalog/skills" },
        { "GET", "/api/users" },
        { "DELETE", "/api/experts/00000000-0000-0000-0000-000000000001" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Rejects_requests_without_a_session_token(string method, string path)
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The staff-only subset. Since P1T-182 a few endpoints above are deliberately shared — the
    /// catalog's reads, and an Expert's own row — so those move here as their own list rather than
    /// weakening the rule for everything else. What stays is what would leak other people's data:
    /// the whole roster, user administration, deletion, and the rendered CV of any id you name.
    /// </summary>
    public static TheoryData<string, string> ServiceManagerOnlyEndpoints() => new()
    {
        { "GET", "/api/experts" },
        { "GET", "/api/experts/00000000-0000-0000-0000-000000000001/cv" },
        { "GET", "/api/experts/00000000-0000-0000-0000-000000000001/cv.pdf" },
        { "GET", "/api/users" },
        { "DELETE", "/api/experts/00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/catalog/categories" },
        { "POST", "/api/catalog/skills" },
        { "PUT", "/api/catalog/skills/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/catalog/categories/00000000-0000-0000-0000-000000000001" },
    };

    /// <summary>
    /// The role split. An Expert token is a *valid* session — correct signature, live account,
    /// current token version — so 403 (or 401) here is the authorization decision itself rather than
    /// a rejected credential. This is the criterion the whole slice exists for: a signed-in Expert
    /// reaches none of the staff surface.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServiceManagerOnlyEndpoints))]
    public async Task Refuses_an_expert_token_on_every_service_manager_endpoint(string method, string path)
    {
        using var expert = factory.CreateExpertClient();

        var response = await expert.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Revocation. The same token, unchanged and unexpired, stops working the moment the account's
    /// token version moves — which is what makes "sign this person out now" possible at all. The
    /// Agents host is held to the same rule by its own test.
    /// </summary>
    [Fact]
    public async Task A_superseded_token_version_refuses_a_previously_valid_token()
    {
        var (client, account) = factory.CreateClientFor(UserRole.ServiceManager);
        using var _ = client;

        (await client.GetAsync("/api/experts")).StatusCode.Should().Be(HttpStatusCode.OK);

        factory.RevokeSessions(account.Id);

        (await client.GetAsync("/api/experts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A token the host cannot check for revocation is not accepted — otherwise dropping the claim
    /// would be a way to opt out of revocation entirely.
    /// </summary>
    [Fact]
    public async Task Refuses_a_token_whose_account_no_longer_exists()
    {
        var (client, account) = factory.CreateClientFor(UserRole.ServiceManager);
        using var _ = client;

        factory.DeleteAccount(account.Id);

        (await client.GetAsync("/api/experts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_the_wrong_key()
    {
        using var client = factory.CreateClient();
        // A structurally valid JWT whose signature was made with a different key.
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiaXNzIjoiY3ZtYW5hZ2VyIiwiYXVkIjoiY3ZtYW5hZ2VyLWFwcCJ9.Ke4Yb5xHhX1fMPbqNQ3-7bOZbnI0Xz6mTgQZ0xhFqUo");

        var response = await client.GetAsync("/api/experts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Allows_the_authenticated_session()
    {
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/experts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The auth ceremonies must stay reachable anonymously — they are how a caller gets a token in
    /// the first place. A rejected payload here still proves the endpoint was reached, not blocked.
    /// </summary>
    [Theory]
    [InlineData("/api/auth/signin/begin")]
    [InlineData("/api/auth/signup/begin")]
    [InlineData("/api/auth/recover/begin")]
    public async Task Auth_ceremonies_stay_anonymous(string path)
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(path, new { email = "nobody@example.com" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
