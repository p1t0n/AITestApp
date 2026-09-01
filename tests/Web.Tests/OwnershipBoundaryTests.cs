using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// Own-row-only, at the HTTP boundary (P1T-182). Eleven of the seventeen child endpoints address a
/// row by its own id with no expert anywhere in the URL — <c>PUT /api/languages/{id}</c>,
/// <c>PATCH /api/achievements/{id}</c> — so the only honest way to ask "can Expert A reach Expert
/// B's data?" is to send A's real session at B's real ids and read the status code.
///
/// <para>Every refusal must be a <b>404</b>. A 403 would confirm that the id exists, which is
/// itself a leak: on a roster of consultants, "that id is real" is information about a person.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class OwnershipBoundaryTests(WebApiFactory factory)
{
    /// <summary>Every route that names one of B's rows by id, with the verb that reaches it.</summary>
    private static IEnumerable<(string Method, string Path)> ForeignRoutes(Fixture b) =>
    [
        ("GET", $"/api/experts/{b.ExpertId}"),
        ("PUT", $"/api/experts/{b.ExpertId}"),
        ("GET", $"/api/experts/{b.ExpertId}/availability"),
        ("POST", $"/api/experts/{b.ExpertId}/availability"),
        ("POST", $"/api/experts/{b.ExpertId}/languages"),
        ("POST", $"/api/experts/{b.ExpertId}/skills"),
        ("POST", $"/api/experts/{b.ExpertId}/qualifications"),
        ("POST", $"/api/experts/{b.ExpertId}/experiences"),
        ("PUT", $"/api/languages/{b.LanguageId}"),
        ("DELETE", $"/api/languages/{b.LanguageId}"),
        ("PUT", $"/api/availability/{b.AvailabilityId}"),
        ("DELETE", $"/api/availability/{b.AvailabilityId}"),
        ("PUT", $"/api/expert-skills/{b.ExpertSkillId}"),
        ("DELETE", $"/api/expert-skills/{b.ExpertSkillId}"),
        ("PUT", $"/api/qualifications/{b.QualificationId}"),
        ("DELETE", $"/api/qualifications/{b.QualificationId}"),
        ("PUT", $"/api/experiences/{b.ExperienceId}"),
        ("DELETE", $"/api/experiences/{b.ExperienceId}"),
        // Two hops from its expert, and no expert in the URL: the easiest route to leave open.
        ("PATCH", $"/api/achievements/{b.AchievementId}"),
    ];

    [Fact]
    public async Task Expert_A_cannot_reach_any_of_expert_Bs_rows_and_every_refusal_is_a_404()
    {
        var staff = factory.CreateAuthenticatedClient();
        var a = await Fixture.CreateAsync(staff, "owner-a");
        var b = await Fixture.CreateAsync(staff, "owner-b");
        var (clientA, _) = factory.CreateExpertClientOwning(a.ExpertId);
        using var _ = clientA;

        var refusals = new List<string>();
        foreach (var (method, path) in ForeignRoutes(b))
        {
            var response = await clientA.SendAsync(Request(method, path));
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                refusals.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        refusals.Should().BeEmpty(
            "every one of B's rows must answer A as though it did not exist — not 403, which would " +
            "confirm the id, and certainly not 200");
    }

    [Fact]
    public async Task Expert_A_reaches_their_own_row_and_its_children()
    {
        var staff = factory.CreateAuthenticatedClient();
        var a = await Fixture.CreateAsync(staff, "self");
        var (clientA, _) = factory.CreateExpertClientOwning(a.ExpertId);
        using var _ = clientA;

        // Keeps the refusals above honest: they must come from ownership, not from the whole
        // Expert-facing surface being closed.
        (await clientA.GetAsync($"/api/experts/{a.ExpertId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.GetAsync($"/api/experts/{a.ExpertId}/availability"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.PatchAsJsonAsync(
                $"/api/achievements/{a.AchievementId}", new { text = "Rewrote my own bullet" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.PutAsJsonAsync(
                $"/api/languages/{a.LanguageId}", new { language = "Welsh", level = "Fluent" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Registered, claim not approved: the Expert owns no row at all. Every own-row endpoint has to
    /// answer identically to the foreign case, or the difference between "pending" and "not yours"
    /// becomes a way to probe the roster.
    /// </summary>
    [Fact]
    public async Task An_expert_who_owns_no_row_gets_the_same_404_everywhere()
    {
        var staff = factory.CreateAuthenticatedClient();
        var someone = await Fixture.CreateAsync(staff, "unclaimed");
        using var unclaimed = factory.CreateExpertClient();

        var statuses = new List<HttpStatusCode>();
        foreach (var (method, path) in ForeignRoutes(someone))
        {
            statuses.Add((await unclaimed.SendAsync(Request(method, path))).StatusCode);
        }

        statuses.Should().OnlyContain(status => status == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_expert_reads_the_catalog_but_cannot_write_it()
    {
        using var expert = factory.CreateExpertClient();

        (await expert.GetAsync("/api/catalog/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await expert.GetAsync("/api/catalog/categories/tree")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await expert.GetAsync("/api/catalog/skills")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A category rename rewrites every CV in the product, so it stays with staff.
        (await expert.PostAsJsonAsync("/api/catalog/categories", new { name = "Mine now", parentId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Database truth, not service convention: the unique partial index refuses a second row for the
    /// same account. Asserted against Postgres, because EF InMemory has no indexes at all and would
    /// let this through — the one place where testing against the real engine is the whole point.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_second_row_for_the_same_owner()
    {
        var staff = factory.CreateAuthenticatedClient();
        var first = await Fixture.CreateAsync(staff, "one-row");
        var second = await Fixture.CreateAsync(staff, "two-rows");
        var (client, account) = factory.CreateExpertClientOwning(first.ExpertId);
        client.Dispose();

        var act = () => factory.SetOwner(second.ExpertId, account.Id);

        act.Should().Throw<Microsoft.EntityFrameworkCore.DbUpdateException>(
                "one person, one row — and the index is what says so")
            .WithInnerException<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23505", "a unique-violation, from the partial index itself");
    }

    /// <summary>An unclaimed row is not an error: any number of rows may have no owner at all.</summary>
    [Fact]
    public async Task Many_rows_may_stay_unclaimed()
    {
        var staff = factory.CreateAuthenticatedClient();
        var first = await Fixture.CreateAsync(staff, "unowned-1");
        var second = await Fixture.CreateAsync(staff, "unowned-2");

        factory.OwnerOf(first.ExpertId).Should().BeNull();
        factory.OwnerOf(second.ExpertId).Should().BeNull();
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            // A payload the endpoint would accept if it let the caller through, so a 404 can only
            // be the ownership decision and never a 400 in disguise.
            request.Content = JsonContent.Create(Payload(path), options: WebApiFactory.Json);
        }

        return request;
    }

    private static object Payload(string path) => path switch
    {
        _ when path.Contains("/languages") => new { language = "Welsh", level = "Fluent" },
        _ when path.Contains("/availability") => new { effectiveFrom = "2030-01-01", capacityPercent = 50 },
        _ when path.Contains("/expert-skills") || path.EndsWith("/skills") =>
            new { skillId = Guid.NewGuid(), level = "Advanced", yearsExperience = 3 },
        _ when path.Contains("/qualifications") =>
            new { type = "Degree", name = "BSc" },
        _ when path.Contains("/experiences") =>
            new { company = "Acme", title = "Engineer", startDate = "2021-01-01", achievements = Array.Empty<object>(), skillIds = Array.Empty<Guid>() },
        _ when path.Contains("/achievements") => new { text = "A rewritten bullet" },
        _ => new
        {
            firstName = "Taken",
            lastName = "Over",
            title = "Engineer",
            email = ApiClientExtensions.UniqueEmail("taken"),
        },
    };

    /// <summary>One expert with one of each child, created through the API as staff.</summary>
    private sealed record Fixture(
        Guid ExpertId,
        Guid LanguageId,
        Guid AvailabilityId,
        Guid ExpertSkillId,
        Guid QualificationId,
        Guid ExperienceId,
        Guid AchievementId)
    {
        public static async Task<Fixture> CreateAsync(HttpClient staff, string prefix)
        {
            var expert = await staff.CreateExpertAsync(
                ApiClientExtensions.NewExpert(email: ApiClientExtensions.UniqueEmail(prefix)));

            var language = await (await staff.PostAsJsonAsync(
                $"/api/experts/{expert.Id}/languages",
                new { language = "English", level = "Native" },
                WebApiFactory.Json)).ReadOkAsync<SpokenLanguageDto>();

            var availability = await (await staff.PostAsJsonAsync(
                $"/api/experts/{expert.Id}/availability",
                new { effectiveFrom = "2026-01-01", capacityPercent = 100 },
                WebApiFactory.Json)).ReadOkAsync<AvailabilityEntryDto>();

            var skills = await (await staff.GetAsync("/api/catalog/skills"))
                .ReadOkAsync<List<Application.Skills.SkillDto>>();
            var expertSkill = await (await staff.PostAsJsonAsync(
                $"/api/experts/{expert.Id}/skills",
                new { skillId = skills[0].Id, level = "Advanced", yearsExperience = 4 },
                WebApiFactory.Json)).ReadOkAsync<ExpertSkillDto>();

            var qualification = await (await staff.PostAsJsonAsync(
                $"/api/experts/{expert.Id}/qualifications",
                new { type = "Degree", name = "BSc Computing" },
                WebApiFactory.Json)).ReadOkAsync<QualificationDto>();

            var experience = await (await staff.PostAsJsonAsync(
                $"/api/experts/{expert.Id}/experiences",
                new
                {
                    company = "Univac",
                    title = "Engineer",
                    startDate = "2019-01-01",
                    achievements = new[] { new { order = 1, text = "Wrote a compiler" } },
                    skillIds = Array.Empty<Guid>(),
                },
                WebApiFactory.Json)).ReadOkAsync<ExperienceDto>();

            return new Fixture(
                expert.Id,
                language.Id,
                availability.Id,
                expertSkill.Id,
                qualification.Id,
                experience.Id,
                experience.Achievements[0].Id);
        }
    }
}
