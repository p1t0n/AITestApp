using System.Net.Http.Json;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// What the closed status hierarchies do with a value outside the set (EXP-76).
///
/// <para>This is the one place the conversion is deliberately <b>stricter</b> than the
/// <c>const string</c> it replaced. A typo'd status used to load as a perfectly ordinary string and
/// then quietly fall through every comparison in the codebase — a scan row stuck at an invented
/// status counts as neither scored, failed nor pending, so its job's progress never reaches
/// total/total and the poll never settles. Now the read throws at the row that holds it, naming the
/// value, which is the difference between a bug report and an investigation.</para>
///
/// <para>Nothing this code writes can produce such a row: the set is closed on the way in. These
/// tests put one there by hand, which is exactly the provenance a value like that would have.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class StatusConversionTests(WebApiFactory factory)
{
    [Fact]
    public async Task A_scan_candidate_status_that_is_not_one_of_the_cases_fails_the_read_rather_than_loading()
    {
        var candidateId = await GivenAScanCandidateAsync();
        await SetStatusAsync("ScoringJobCandidates", "Status", candidateId, "abandoned");

        var read = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.ScoringJobCandidates.AsNoTracking().SingleAsync(c => c.Id == candidateId);
        };

        (await read.Should().ThrowAsync<FormatException>())
            .WithMessage("*abandoned*", "the row that broke is worth naming");
    }

    [Fact]
    public async Task A_contest_outcome_that_is_not_one_of_the_cases_fails_the_read_rather_than_loading()
    {
        var candidateId = await GivenAScanCandidateAsync();
        await SetStatusAsync("ScoringJobCandidates", "ContestOutcome", candidateId, "escalated");

        var read = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.ScoringJobCandidates.AsNoTracking().SingleAsync(c => c.Id == candidateId);
        };

        (await read.Should().ThrowAsync<FormatException>()).WithMessage("*escalated*");
    }

    /// <summary>A null contest outcome is the normal state of every row nobody has reviewed, so the
    /// converter must never see it — EF keeps null as null on both sides.</summary>
    [Fact]
    public async Task An_unreviewed_contest_outcome_stays_null_through_the_converter()
    {
        var candidateId = await GivenAScanCandidateAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ScoringJobCandidates.AsNoTracking().SingleAsync(c => c.Id == candidateId);

        row.ContestOutcome.Should().BeNull();
    }

    /// <summary>The set is closed on the way in too: the review endpoint's 409 for an outcome that
    /// is not one of the two is the behaviour the string guard had, now reached by parsing.</summary>
    [Fact]
    public async Task A_review_outcome_outside_the_set_is_still_refused_with_the_same_409()
    {
        var candidateId = await GivenAScanCandidateAsync();
        var staff = factory.CreateAuthenticatedClient();

        var response = await staff.PostAsJsonAsync(
            $"/api/contests/{candidateId}/review",
            new { outcome = "escalated-to-tribunal", response = "..." });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    private async Task<Guid> GivenAScanCandidateAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expertId = await db.Experts.AsNoTracking().Select(e => e.Id).FirstAsync();
        var candidateId = Guid.NewGuid();
        var job = new ScoringJob
        {
            Id = Guid.NewGuid(),
            JobDescription = "Status conversion",
            ChunkSize = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        job.Candidates.Add(new ScoringJobCandidate
        {
            Id = candidateId,
            ExpertId = expertId,
            Name = "Status conversion",
            Title = "Engineer",
            Digest = "A digest.",
        });
        db.ScoringJobs.Add(job);
        await db.SaveChangesAsync();
        return candidateId;
    }

    private async Task SetStatusAsync(string table, string column, Guid id, string value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A local, not an interpolated argument: EF1002 flags the latter, and the only
        // caller-supplied value here travels as the {0} parameter either way.
        var sql = $$"""UPDATE "{{table}}" SET "{{column}}" = {0} WHERE "Id" = {1}""";
        await db.Database.ExecuteSqlRawAsync(sql, value, id);
    }
}
