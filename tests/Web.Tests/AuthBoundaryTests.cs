using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace CvManager.Web.Tests;

/// <summary>
/// The service boundary itself. The app-wide authorization fallback policy is what makes the REST
/// API authenticated; nothing on a controller says so, so only a request without a token proves it.
/// These are the executable answer to "is the Web API protected at the boundary?".
/// </summary>
[Collection(WebApiCollection.Name)]
public class AuthBoundaryTests(WebApiFactory factory)
{
    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/employees" },
        { "GET", "/api/employees/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/employees/00000000-0000-0000-0000-000000000001/cv" },
        { "GET", "/api/employees/00000000-0000-0000-0000-000000000001/cv.pdf" },
        { "GET", "/api/catalog/categories" },
        { "GET", "/api/catalog/categories/tree" },
        { "GET", "/api/catalog/skills" },
        { "GET", "/api/users" },
        { "DELETE", "/api/employees/00000000-0000-0000-0000-000000000001" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Rejects_requests_without_a_session_token(string method, string path)
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_the_wrong_key()
    {
        using var client = factory.CreateClient();
        // A structurally valid JWT whose signature was made with a different key.
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiaXNzIjoiY3ZtYW5hZ2VyIiwiYXVkIjoiY3ZtYW5hZ2VyLWFwcCJ9.Ke4Yb5xHhX1fMPbqNQ3-7bOZbnI0Xz6mTgQZ0xhFqUo");

        var response = await client.GetAsync("/api/employees");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Allows_the_authenticated_session()
    {
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/employees");

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
