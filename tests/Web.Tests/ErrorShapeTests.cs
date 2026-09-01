using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpertToJob.Application.Employees;
using ExpertToJob.Application.Skills;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// What a caller sees when it gets things wrong. The REST error shapes are the sibling of the MCP
/// server's structured errors (<c>not_found</c> / <c>conflict</c> / <c>validation</c>) — both are
/// thin adapters over the same Application-layer exceptions, so they must agree on which failure is
/// which.
/// </summary>
[Collection(WebApiCollection.Name)]
public class ErrorShapeTests(WebApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task A_missing_employee_is_404_problem_details()
    {
        var response = await _client.GetAsync($"/api/employees/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.ReadAsync<ProblemDetails>();
        problem.Title.Should().Be("Resource not found");
        problem.Status.Should().Be(404);
    }

    [Fact]
    public async Task A_validation_failure_is_400_with_per_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/employees",
            new SaveEmployeeDto("", "Hopper", "Engineer", "not-an-email", null, null, null, null),
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().Should().Be("Validation failed");
        var errors = doc.RootElement.GetProperty("errors");
        errors.TryGetProperty("FirstName", out _).Should().BeTrue();
        errors.TryGetProperty("Email", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_malformed_body_is_400_and_never_reaches_the_Application_layer()
    {
        var response = await _client.PostAsync("/api/employees",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_non_guid_route_value_is_404_rather_than_a_server_error()
    {
        // The `:guid` route constraint has to reject it — an unconstrained bind would surface as a
        // 500 from the model binder instead.
        var response = await _client.GetAsync("/api/employees/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_duplicate_active_email_is_409_conflict()
    {
        // Postgres-only ground: the uniqueness lives in a partial index over Active rows, so this
        // failure exists nowhere an EF InMemory test could see it.
        var email = ApiClientExtensions.UniqueEmail("clash");
        await _client.CreateEmployeeAsync(ApiClientExtensions.NewEmployee(
            firstName: "First", lastName: "Claimant", email: email));

        var response = await _client.PostAsJsonAsync("/api/employees",
            ApiClientExtensions.NewEmployee(firstName: "Second", lastName: "Claimant", email: email),
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadAsync<ProblemDetails>();
        problem.Title.Should().Be("Conflict");
        problem.Detail.Should().Contain(email);
    }

    [Fact]
    public async Task A_skill_pointing_at_a_missing_category_is_404_not_a_foreign_key_500()
    {
        var response = await _client.PostAsJsonAsync("/api/catalog/skills",
            new SaveSkillDto("Ada", Guid.NewGuid()), WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
