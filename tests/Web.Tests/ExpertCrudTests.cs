using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The expert resource end to end over real Postgres: the round trip, the two update verbs, the
/// child sub-resources, and the cascade a delete performs in the database rather than in EF's
/// change tracker.
/// </summary>
[Collection(WebApiCollection.Name)]
public class ExpertCrudTests(WebApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Create_returns_201_with_a_location_header_and_the_row_reads_back()
    {
        var dto = ApiClientExtensions.NewExpert(
            firstName: "  Grace  ", lastName: "Hopper", title: "Rear Admiral", location: "Arlington");

        var created = await _client.PostAsJsonAsync("/api/experts", dto, WebApiFactory.Json);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();

        var body = await created.ReadAsync<ExpertDetailDto>();
        body.FirstName.Should().Be("Grace", "the Application layer trims names on the way in");
        body.Status.Should().Be(ExpertStatus.Active);

        var fetched = await _client.GetFromJsonAsync<ExpertDetailDto>(
            created.Headers.Location, WebApiFactory.Json);
        fetched!.Id.Should().Be(body.Id);
        fetched.Location.Should().Be("Arlington");
    }

    [Fact]
    public async Task Put_replaces_every_field_including_the_ones_left_out()
    {
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Alan", lastName: "Turing", phone: "+44 100", location: "Bletchley",
            summary: "Cryptanalysis", photoUrl: "https://example.com/a.png"));

        var response = await _client.PutAsJsonAsync(
            $"/api/experts/{expert.Id}",
            new SaveExpertDto("Alan", "Turing", "Fellow", expert.Email, null, null, null, null),
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ReadAsync<ExpertDetailDto>();
        updated.Title.Should().Be("Fellow");
        updated.Phone.Should().BeNull("PUT is a full replace — omitted optional fields are cleared");
        updated.Location.Should().BeNull();
        updated.Summary.Should().BeNull();
        updated.PhotoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Patch_changes_only_the_fields_present_in_the_body()
    {
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Katherine", lastName: "Johnson", title: "Mathematician",
            phone: "+1 555", location: "Hampton", summary: "Orbital mechanics"));

        var response = await _client.PatchAsJsonAsync(
            $"/api/experts/{expert.Id}",
            new { title = "Senior Mathematician" },
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await response.ReadAsync<ExpertDetailDto>();
        patched.Title.Should().Be("Senior Mathematician");
        patched.Phone.Should().Be("+1 555", "a field absent from a PATCH body keeps its value");
        patched.Location.Should().Be("Hampton");
        patched.Summary.Should().Be("Orbital mechanics");
        patched.Email.Should().Be(expert.Email);
    }

    [Fact]
    public async Task Delete_removes_the_expert_and_cascades_to_its_children()
    {
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Barbara", lastName: "Liskov"));

        var language = await (await _client.PostAsJsonAsync(
            $"/api/experts/{expert.Id}/languages",
            new SaveSpokenLanguageDto("English", LanguageLevel.Native), WebApiFactory.Json))
            .ReadOkAsync<SpokenLanguageDto>();

        var experience = await (await _client.PostAsJsonAsync(
            $"/api/experts/{expert.Id}/experiences",
            new SaveExperienceDto("MIT", "Professor", null, new DateOnly(1975, 1, 1), null, null,
                [new SaveAchievementDto(1, "Defined the substitution principle.")], []),
            WebApiFactory.Json))
            .ReadOkAsync<ExperienceDto>();

        (await _client.DeleteAsync($"/api/experts/{expert.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/api/experts/{expert.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The children go with it: updating either one now finds nothing. This is the database's
        // cascade, not EF's in-memory graph — the only place it can be observed.
        (await _client.PutAsJsonAsync($"/api/languages/{language.Id}",
            new SaveSpokenLanguageDto("English", LanguageLevel.Fluent), WebApiFactory.Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.DeleteAsync($"/api/experiences/{experience.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Experiences_round_trip_with_their_achievements_from_a_json_body()
    {
        // The bodies here are the whole point: every experience field arrives as JSON, so an
        // endpoint that binds the DTO from anywhere else (a controller without [ApiController]
        // infers form/query) silently receives an empty record and fails validation.
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Frances", lastName: "Allen"));

        var created = await (await _client.PostAsJsonAsync(
            $"/api/experts/{expert.Id}/experiences",
            new SaveExperienceDto("IBM Research", "Fellow", "Yorktown Heights",
                new DateOnly(1957, 1, 1), new DateOnly(2002, 1, 1), "Compiler optimization.",
                [new SaveAchievementDto(1, "Pioneered optimizing compilers.")], []),
            WebApiFactory.Json)).ReadOkAsync<ExperienceDto>();

        created.Company.Should().Be("IBM Research");
        created.Achievements.Should().ContainSingle().Which.Text.Should().Be("Pioneered optimizing compilers.");

        var updated = await (await _client.PutAsJsonAsync($"/api/experiences/{created.Id}",
            new SaveExperienceDto("IBM Research", "IBM Fellow", "Yorktown Heights",
                new DateOnly(1957, 1, 1), new DateOnly(2002, 1, 1), "Compiler optimization.",
                [new SaveAchievementDto(1, "Pioneered optimizing compilers.")], []),
            WebApiFactory.Json)).ReadOkAsync<ExperienceDto>();

        updated.Title.Should().Be("IBM Fellow");

        // An end date before the start date is the validator's business, and it must be reported as
        // a 400 rather than stored.
        (await _client.PutAsJsonAsync($"/api/experiences/{created.Id}",
            new SaveExperienceDto("IBM Research", "IBM Fellow", null,
                new DateOnly(2002, 1, 1), new DateOnly(1957, 1, 1), null, [], []),
            WebApiFactory.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Availability_entries_round_trip_as_a_step_function()
    {
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Radia", lastName: "Perlman"));

        foreach (var (from, capacity) in new[]
                 {
                     (new DateOnly(2020, 1, 1), 100),
                     (new DateOnly(2020, 6, 1), 50),
                 })
        {
            (await _client.PostAsJsonAsync($"/api/experts/{expert.Id}/availability",
                new SaveAvailabilityEntryDto(from, capacity), WebApiFactory.Json))
                .EnsureSuccessStatusCode();
        }

        var entries = await _client.GetFromJsonAsync<List<AvailabilityEntryDto>>(
            $"/api/experts/{expert.Id}/availability", WebApiFactory.Json);

        entries!.Select(e => (e.EffectiveFrom, e.CapacityPercent))
            .Should().Equal((new DateOnly(2020, 1, 1), 100), (new DateOnly(2020, 6, 1), 50));

        // The step function's current value is what the roster list reports.
        var detail = await _client.GetFromJsonAsync<ExpertDetailDto>(
            $"/api/experts/{expert.Id}", WebApiFactory.Json);
        detail!.CurrentCapacityPercent.Should().Be(50);
    }

    [Fact]
    public async Task Drafts_are_hidden_from_the_roster_until_promoted()
    {
        // The API has no draft-create verb — drafts are agent-staged over MCP — so this exercises
        // the human half: the promote gate and the includeDrafts switch.
        var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
            firstName: "Jean", lastName: "Bartik"));

        var roster = await _client.GetFromJsonAsync<List<ExpertSummaryDto>>(
            "/api/experts", WebApiFactory.Json);
        roster!.Should().Contain(e => e.Id == expert.Id);

        // Promoting an already-Active expert is a documented no-op, not an error.
        (await _client.PostAsync($"/api/experts/{expert.Id}/promote", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The roster list is unpaged, and two features in the SPA depend on it being so: the roster
    /// table filters and sorts client-side, and the ⌘K Command Palette searches people by filtering
    /// the same cached response (P1T-165). "The palette searches the whole roster" is true only for
    /// as long as one request returns the whole roster, so it is asserted here rather than assumed
    /// in a comment — the day this endpoint starts paging, this test fails and both surfaces need a
    /// server-side search instead.
    /// </summary>
    [Fact]
    public async Task Roster_list_returns_every_active_expert_in_one_response()
    {
        // Comfortably past any default page size a paging library would introduce.
        var created = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var expert = await _client.CreateExpertAsync(ApiClientExtensions.NewExpert(
                firstName: "Unpaged", lastName: $"Roster{i:D2}"));
            created.Add(expert.Id);
        }

        var roster = await _client.GetFromJsonAsync<List<ExpertSummaryDto>>(
            "/api/experts", WebApiFactory.Json);

        roster!.Select(e => e.Id).Should().Contain(created,
            "the palette and the roster table both read this one response as the whole roster");
    }
}
