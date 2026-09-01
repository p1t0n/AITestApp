using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Employees;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The two CV surfaces over the wire: the JSON projection the SPA renders, and the server-side PDF
/// render that shares it (P1T-139). Both are assembled below the Web layer, so what is asserted here
/// is the adapter's half — status, content type, filename, and that the projection reaches the
/// renderer intact.
/// </summary>
[Collection(WebApiCollection.Name)]
public class CvEndpointTests(WebApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private async Task<EmployeeDetailDto> SeedCvSubjectAsync()
    {
        var employee = await _client.CreateEmployeeAsync(ApiClientExtensions.NewEmployee(
            firstName: "Margaret", lastName: "Hamilton", title: "Software Engineer",
            location: "Cambridge, MA", summary: "Apollo guidance software."));

        (await _client.PostAsJsonAsync($"/api/employees/{employee.Id}/experiences",
            new SaveExperienceDto("MIT Instrumentation Lab", "Lead Engineer", "Cambridge, MA",
                new DateOnly(1965, 1, 1), new DateOnly(1972, 1, 1), "Onboard flight software.",
                [new SaveAchievementDto(1, "Led the team that wrote the Apollo onboard software.")], []),
            WebApiFactory.Json)).EnsureSuccessStatusCode();

        (await _client.PostAsJsonAsync($"/api/employees/{employee.Id}/qualifications",
            new SaveQualificationDto(QualificationType.Degree, "BA Mathematics", "Earlham College",
                "Mathematics", new DateOnly(1954, 9, 1), new DateOnly(1958, 6, 1), null, null, null, null),
            WebApiFactory.Json)).EnsureSuccessStatusCode();

        return employee;
    }

    [Fact]
    public async Task Cv_projection_carries_the_employee_and_its_sections()
    {
        var employee = await SeedCvSubjectAsync();

        var cv = await _client.GetFromJsonAsync<CvDto>(
            $"/api/employees/{employee.Id}/cv", WebApiFactory.Json);

        cv!.FullName.Should().Be("Margaret Hamilton");
        cv.Experiences.Should().ContainSingle()
            .Which.Achievements.Should().ContainSingle()
            .Which.Id.Should().NotBeEmpty("bullet ids are the join key downstream tools use");
        cv.Education.Should().ContainSingle().Which.Name.Should().Be("BA Mathematics");
        cv.Certifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Cv_pdf_is_served_as_a_pdf_download_named_after_the_employee()
    {
        var employee = await SeedCvSubjectAsync();

        var response = await _client.GetAsync($"/api/employees/{employee.Id}/cv.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Be("margaret-hamilton-cv.pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public async Task Cv_endpoints_are_404_for_an_unknown_employee()
    {
        var missing = Guid.NewGuid();

        (await _client.GetAsync($"/api/employees/{missing}/cv"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/employees/{missing}/cv.pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
