using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The Art. 22(3) safeguards (P1T-189). We concede the scoring is automated and rely on
/// Art. 22(2)(a), so these three are obligations: a human looks, the person may say why, and the
/// outcome is recorded.
///
/// <para>What is asserted is the round trip — contest, queue, review, recorded — and the two
/// boundaries around it: somebody can only contest a score about themselves, and erasure takes a
/// pending contest with it.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class ContestTests(WebApiFactory factory)
{
    /// <summary>
    /// The whole safeguard, end to end. The evidence that a human intervened is the recorded
    /// review — an intervention nobody can evidence did not happen, as far as an audit goes.
    /// </summary>
    [Fact]
    public async Task A_contested_score_reaches_the_queue_and_a_recorded_human_outcome_lands_on_it()
    {
        var world = await GivenAScoredPersonAsync();
        var staff = factory.CreateAuthenticatedClient();

        var contested = await (await world.Client.PostAsJsonAsync(
                "/api/contests",
                new { scoringCandidateId = world.CandidateId, view = "I led that platform, not just used it." }))
            .ReadOkAsync<ContestQueueItemDto>();

        contested.ScoringCandidateId.Should().Be(world.CandidateId);
        contested.View.Should().Contain("I led that platform");

        var queue = await (await staff.GetAsync("/api/contests")).ReadOkAsync<List<ContestQueueItemDto>>();
        var item = queue.Single(c => c.ScoringCandidateId == world.CandidateId);

        item.Score.Should().Be(41, "the reviewer needs the decision in front of them");
        item.Rationale.Should().NotBeNullOrWhiteSpace();
        item.View.Should().Contain("I led that platform", "and what the person said about it");
        item.JobDescription.Should().Contain("Payments platform");

        var review = await (await staff.PostAsJsonAsync(
                $"/api/contests/{world.CandidateId}/review",
                new { outcome = ContestOutcome.Overturned, response = "Agreed — shortlisting you by hand." }))
            .ReadOkAsync<ContestReviewDto>();

        review.Outcome.Should().Be(ContestOutcome.Overturned);
        review.ReviewedByUserId.Should().NotBeNull("a person, named, is the point of the safeguard");

        var row = await CandidateAsync(world.CandidateId);
        row.ContestOutcome.Should().Be(ContestOutcome.Overturned);
        row.ContestReviewedAt.Should().NotBeNull();
        row.ContestResponse.Should().Contain("shortlisting you by hand");

        (await (await staff.GetAsync("/api/contests")).ReadOkAsync<List<ContestQueueItemDto>>())
            .Should().NotContain(c => c.ScoringCandidateId == world.CandidateId,
                "reviewed items leave the queue; the record of the review stays on the row");
    }

    /// <summary>
    /// Asking for a human to look is a right on its own. Requiring an explanation first would be a
    /// toll on it, so the view is optional.
    /// </summary>
    [Fact]
    public async Task A_person_can_contest_without_explaining_themselves()
    {
        var world = await GivenAScoredPersonAsync();

        var response = await world.Client.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CandidateAsync(world.CandidateId)).ContestedAt.Should().NotBeNull();
    }

    /// <summary>
    /// A human looked and the person still disagrees. Refusing to hear that would make the
    /// safeguard a one-shot form, so contesting again reopens it rather than being rejected.
    /// </summary>
    [Fact]
    public async Task Contesting_again_after_a_review_reopens_it()
    {
        var world = await GivenAScoredPersonAsync();
        var staff = factory.CreateAuthenticatedClient();

        await world.Client.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = "First time." });
        await staff.PostAsJsonAsync(
            $"/api/contests/{world.CandidateId}/review",
            new { outcome = ContestOutcome.Upheld, response = "The score stands." });

        await world.Client.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = "Still wrong, and here is why." });

        var row = await CandidateAsync(world.CandidateId);
        row.ContestOutcome.Should().BeNull("the previous conclusion no longer stands unchallenged");
        row.ContestNote.Should().Contain("Still wrong");

        (await (await staff.GetAsync("/api/contests")).ReadOkAsync<List<ContestQueueItemDto>>())
            .Should().Contain(c => c.ScoringCandidateId == world.CandidateId);
    }

    // ---- The boundaries ----------------------------------------------------------------------------

    /// <summary>
    /// A scan row is a decision about one person. Contesting somebody else's is a 404 rather than a
    /// 403, because the alternative confirms that a score about that person exists.
    /// </summary>
    [Fact]
    public async Task An_expert_can_only_contest_a_score_about_themselves()
    {
        var world = await GivenAScoredPersonAsync();
        using var stranger = factory.CreateExpertClient();

        var response = await stranger.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = "Not mine." });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CandidateAsync(world.CandidateId)).ContestedAt.Should().BeNull();
    }

    [Fact]
    public async Task The_queue_and_the_review_are_staff_only()
    {
        var world = await GivenAScoredPersonAsync();

        (await world.Client.GetAsync("/api/contests")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await world.Client.PostAsJsonAsync(
                $"/api/contests/{world.CandidateId}/review",
                new { outcome = ContestOutcome.Upheld, response = "Mine now." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "the person cannot be the human who reviews their own score");
    }

    [Fact]
    public async Task An_invented_outcome_is_refused()
    {
        var world = await GivenAScoredPersonAsync();
        var staff = factory.CreateAuthenticatedClient();

        await world.Client.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = "Please look." });

        (await staff.PostAsJsonAsync(
                $"/api/contests/{world.CandidateId}/review",
                new { outcome = "escalated-to-tribunal", response = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reviewing_a_score_nobody_contested_is_refused()
    {
        var world = await GivenAScoredPersonAsync();
        var staff = factory.CreateAuthenticatedClient();

        (await staff.PostAsJsonAsync(
                $"/api/contests/{world.CandidateId}/review",
                new { outcome = ContestOutcome.Upheld, response = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Erasure wins (P1T-186). The contest was a request <em>by</em> somebody who has now withdrawn
    /// entirely, so the queue simply loses the item — correct, not lossy. It falls out of the scan
    /// row being deleted whole, which is why nothing here had to be written for it.
    /// </summary>
    [Fact]
    public async Task Erasure_takes_a_pending_contest_with_it()
    {
        var world = await GivenAScoredPersonAsync();
        var staff = factory.CreateAuthenticatedClient();

        await world.Client.PostAsJsonAsync(
            "/api/contests", new { scoringCandidateId = world.CandidateId, view = "Please look." });
        (await (await staff.GetAsync("/api/contests")).ReadOkAsync<List<ContestQueueItemDto>>())
            .Should().Contain(c => c.ScoringCandidateId == world.CandidateId);

        SetControlWord(world.OwnerId);
        await world.Client.PostAsJsonAsync(
            "/api/me/account/erase", new { controlWord = "correct-horse-battery-staple" });

        (await (await staff.GetAsync("/api/contests")).ReadOkAsync<List<ContestQueueItemDto>>())
            .Should().NotContain(c => c.ScoringCandidateId == world.CandidateId);
    }

    // ---- Fixture -------------------------------------------------------------------------------------

    private sealed record World(HttpClient Client, Guid ExpertId, Guid OwnerId, Guid CandidateId);

    /// <summary>Somebody the scan has scored badly, with an account of their own — which is the only
    /// population that has anything to contest, because only a claimed record is scanned at all.</summary>
    private async Task<World> GivenAScoredPersonAsync()
    {
        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert(firstName: "Quill"));
        var account = factory.CreateAccount(UserRole.Expert);
        factory.SetOwner(expert.Id, account.Id);

        var candidateId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new ScoringJob
            {
                Id = Guid.NewGuid(),
                JobDescription = "Payments platform lead",
                State = ScoringJobState.Completed,
                ChunkSize = 10,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            job.Candidates.Add(new ScoringJobCandidate
            {
                Id = candidateId,
                ExpertId = expert.Id,
                Name = "Quill Lovelace",
                Title = "Engineer",
                Digest = "Payments, platforms.",
                Status = ScoringCandidateStatus.Scored,
                Score = 41,
                Band = "weak",
                Rationale = "Looks like a user of payment platforms rather than a builder of them.",
            });
            db.ScoringJobs.Add(job);
            await db.SaveChangesAsync();
        }

        return new World(factory.ClientForAccount(account), expert.Id, account.Id, candidateId);
    }

    private async Task<ScoringJobCandidate> CandidateAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScoringJobCandidates.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private void SetControlWord(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Id == userId).ControlWordHash =
            new ExpertToJob.Web.Auth.ControlWordHasher().Hash("correct-horse-battery-staple");
        db.SaveChanges();
    }
}
