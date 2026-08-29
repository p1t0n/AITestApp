using System.Net.Http.Json;
using CvManager.Application.Employees;

namespace CvManager.Web.Tests;

/// <summary>Small helpers so a test reads as the story it tells, not as HTTP plumbing.</summary>
internal static class ApiClientExtensions
{
    /// <summary>A unique address per call — the roster carries a partial unique index on the email
    /// of Active employees, and the suite shares one database.</summary>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public static async Task<EmployeeDetailDto> CreateEmployeeAsync(
        this HttpClient client, SaveEmployeeDto dto)
    {
        var response = await client.PostAsJsonAsync("/api/employees", dto, WebApiFactory.Json);
        return await response.ReadOkAsync<EmployeeDetailDto>();
    }

    public static SaveEmployeeDto NewEmployee(
        string firstName = "Ada",
        string lastName = "Lovelace",
        string title = "Engineer",
        string? email = null,
        string? phone = null,
        string? location = null,
        string? summary = null,
        string? photoUrl = null) =>
        new(firstName, lastName, title, email ?? UniqueEmail("ada"), phone, location, summary, photoUrl);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(WebApiFactory.Json))!;

    /// <summary>
    /// Reads a successful response's body, failing with the server's own words when the call did not
    /// succeed. Deserializing an error payload into a DTO otherwise yields a default-filled object
    /// and the test carries on asserting against nothing.
    /// </summary>
    public static async Task<T> ReadOkAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new Xunit.Sdk.XunitException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery} " +
                $"returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.ReadAsync<T>();
    }
}
