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
/// The retention sweep against real Postgres (P1T-188). It runs here rather than in-memory because
/// the thing it triggers is the erasure path, and that path's whole subject is cascades and jsonb —
/// a sweep test that could not see them would be asserting that nothing it cannot see is gone.
///
/// <para>The clock is driven, not waited on: whole years pass between assertions.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RetentionTests(WebApiFactory factory)
{
    // ---- The two populations ----------------------------------------------------------------------

    /// <summary>
    /// The unclaimed clock, and the reason it is short: nobody can be told this record exists, so
    /// holding it for two years would be holding somebody who cannot object, export or erase.
    /// </summary>
    [Fact]
    public async Task An_unclaimed_record_is_erased_six_months_after_collection()
    {
        var world = await GivenAPersonAsync(claimed: false);

        (await SweepAtAsync(world.CollectedAt.AddMonths(5))).Expired.Should().Be(0);
        (await ExistsAsync(world.ExpertId)).Should().BeTrue();

        var result = await SweepAtAsync(world.CollectedAt.AddMonths(6).AddDays(1));

        result.Expired.Should().Be(1);
        (await ExistsAsync(world.ExpertId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_claimed_record_survives_six_months_and_is_erased_after_two_years()
    {
        var world = await GivenAPersonAsync(claimed: true);
        await SetActivityAsync(world.ExpertId, world.CollectedAt);

        (await SweepAtAsync(world.CollectedAt.AddMonths(7))).Expired.Should().Be(0);
        (await SweepAtAsync(world.CollectedAt.AddYears(2).AddDays(-1))).Expired.Should().Be(0);
        (await ExistsAsync(world.ExpertId)).Should().BeTrue();

        (await SweepAtAsync(world.CollectedAt.AddYears(2).AddDays(1))).Expired.Should().Be(1);
        (await ExistsAsync(world.ExpertId)).Should().BeFalse();
    }

    /// <summary>An Expert with no activity yet measures from the first <c>ProcessingRecord</c> —
    /// the only date the system has for them.</summary>
    [Fact]
    public async Task A_claimed_record_with_no_activity_measures_from_collection()
    {
        var world = await GivenAPersonAsync(claimed: true);
        (await LastActivityAsync(world.ExpertId)).Should().BeNull("nothing has been stamped yet");

        (await SweepAtAsync(world.CollectedAt.AddYears(2).AddDays(-1))).Expired.Should().Be(0);
        (await SweepAtAsync(world.CollectedAt.AddYears(2).AddDays(1))).Expired.Should().Be(1);
    }

    // ---- The inversion: who is allowed to move the clock ---------------------------------------------

    /// <summary>
    /// The important one. A Service Manager editing the record and an agent scoring it must not
    /// reset the clock — otherwise a bench running weekly scans keeps everybody alive by looking at
    /// them, and retention means nothing.
    /// </summary>
    [Fact]
    public async Task Neither_a_staff_edit_nor_agent_scoring_moves_the_clock()
    {
        var world = await GivenAPersonAsync(claimed: true);
        var staff = factory.CreateAuthenticatedClient();

        await staff.PatchAsJsonAsync(
            $"/api/experts/{world.ExpertId}", new { title = "Principal Engineer" }, WebApiFactory.Json);
        await staff.PutAsJsonAsync(
            $"/api/experts/{world.ExpertId}",
            new
            {
                firstName = "Edited", lastName = "ByStaff", title = "Staff Edited",
                email = world.Email, phone = (string?)null, location = (string?)null,
                summary = (string?)null, photoUrl = (string?)null,
            },
            WebApiFactory.Json);

        // And an agent writing through the same services: the MCP host resolves every caller as
        // unrestricted, which is exactly the state the stamp refuses to act on.
        await ScoreLikeAnAgentAsync(world.ExpertId);

        (await LastActivityAsync(world.ExpertId)).Should().BeNull(
            "being looked at, scored or corrected by somebody else is not the person being present");
    }

    /// <summary>And the positive half, or the rule above would pass by never stamping at all.</summary>
    [Fact]
    public async Task The_experts_own_write_moves_the_clock()
    {
        var world = await GivenAPersonAsync(claimed: true);
        using var client = factory.ClientForAccount(world.Owner!);

        await client.PostAsJsonAsync("/api/me/visibility/hide", new { });

        var stamped = await LastActivityAsync(world.ExpertId);
        stamped.Should().NotBeNull("pausing is the person saying they are here and choosing");

        // And it is the value the store holds, not one truncated on the way back — the trap
        // HiddenAt hit in P1T-185, which the retention boundary would inherit.
        (await LastActivityAsync(world.ExpertId)).Should().Be(stamped);
    }

    // ---- The demo trap -------------------------------------------------------------------------------

    /// <summary>
    /// Without this the demo roster silently evaporates and every developer's local environment
    /// empties itself. Asserted at an absurd date so it cannot pass by the clock simply not having
    /// reached anything.
    /// </summary>
    [Fact]
    public async Task The_sweep_never_touches_fabricated_records_at_any_date()
    {
        var seeded = await SeedFabricatedAsync();

        var result = await SweepAtAsync(DateTimeOffset.UtcNow.AddYears(50));

        result.Examined.Should().BeGreaterThan(0, "it looked at them and decided");
        (await ExistsAsync(seeded)).Should().BeTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Experts.CountAsync(e => e.Email.EndsWith("@example.com"))).Should().BeGreaterThan(0,
            "the dev seed is still there too");
    }

    // ---- One mechanism, not two ------------------------------------------------------------------------

    /// <summary>
    /// The assertion that keeps retention a <em>trigger</em> rather than a second implementation of
    /// "delete a person". Two people identical but for how they leave: one asks, one runs out of
    /// time, and the database cannot tell the difference afterwards.
    /// </summary>
    [Fact]
    public async Task Expiry_and_a_requested_deletion_leave_the_database_in_the_same_state()
    {
        var byRequest = await GivenAPersonAsync(claimed: true, withLedger: true);
        var byExpiry = await GivenAPersonAsync(claimed: true, withLedger: true);

        SetControlWord(byRequest.Owner!.Id);
        using var client = factory.ClientForAccount(byRequest.Owner);
        await client.PostAsJsonAsync(
            "/api/me/account/erase", new { controlWord = "correct-horse-battery-staple" });

        await SetActivityAsync(byExpiry.ExpertId, byExpiry.CollectedAt);
        await SweepAtAsync(byExpiry.CollectedAt.AddYears(3));

        var requested = await ResidueAsync(byRequest);
        var expired = await ResidueAsync(byExpiry);

        expired.Should().BeEquivalentTo(requested,
            "one mechanism with two triggers — two implementations of deleting a person diverge, "
            + "and it is only a question of when");
    }

    // ---- What the person is told ---------------------------------------------------------------------

    [Fact]
    public async Task The_access_view_carries_the_persons_own_expiry_date_and_the_period()
    {
        var world = await GivenAPersonAsync(claimed: true);
        using var client = factory.ClientForAccount(world.Owner!);

        var view = await (await client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();

        view.RetentionClock.Should().Be(RetentionClock.Claimed);
        view.ExpiresAt.Should().NotBeNull("Art. 15(1)(d) asks for the period; the date is the form "
                                          + "of it somebody can act on");
        view.Retention.Should().Contain("two years");
        view.ExpiringSoon.Should().BeFalse();
    }

    /// <summary>
    /// The banner's data, and the property that makes it kind rather than merely correct: reading
    /// the warning is itself activity, so signing in to see it resets the clock it warns about.
    /// </summary>
    [Fact]
    public async Task An_expert_inside_the_final_thirty_days_is_warned_and_reading_it_cures_it()
    {
        var world = await GivenAPersonAsync(claimed: true);
        using var client = factory.ClientForAccount(world.Owner!);

        // Their last act was almost two years ago.
        var longAgo = DateTimeOffset.UtcNow.AddYears(-2).AddDays(10);
        await SetActivityAsync(world.ExpertId, longAgo);

        var warned = await (await client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();
        warned.ExpiringSoon.Should().BeTrue("ten days left");

        // Doing something — anything of their own — pushes it back out.
        await client.PostAsJsonAsync("/api/me/visibility/hide", new { });

        var after = await (await client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();
        after.ExpiringSoon.Should().BeFalse("the warning cured the thing it warned about");
        after.ExpiresAt.Should().BeAfter(warned.ExpiresAt!.Value);
    }

    // ---- Fixture ---------------------------------------------------------------------------------------

    private sealed record World(
        Guid ExpertId, ExpertToJob.Domain.Entities.User? Owner, string Email, DateTimeOffset CollectedAt)
    {
        public Guid? OwnerId => Owner?.Id;
    }

    /// <summary>
    /// One record with a real address, collected at a fixed moment. <paramref name="withLedger"/>
    /// gives it a scan row and a decided proposal, so the shared-path comparison has residue to
    /// compare.
    /// </summary>
    private async Task<World> GivenAPersonAsync(bool claimed, bool withLedger = false)
    {
        var collectedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // A real domain: the sweep's exclusion is reserved documentation domains, and the roster's
        // own test helper hands out @example.com, which would make every record here immortal.
        var email = $"person-{Guid.NewGuid():N}@lovelace.dev";
        var expertId = Guid.NewGuid();
        ExpertToJob.Domain.Entities.User? owner = null;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expert = new Expert
            {
                Id = expertId,
                Status = ExpertStatus.Active,
                FirstName = "Retained",
                LastName = "Person",
                Title = "Engineer",
                Email = email,
            };
            expert.ProcessingRecords.Add(ProcessingRecord.For(
                expertId, 1, ProcessingOrigin.StaffCreated, null,
                "Added to the bench by a Service Manager.", collectedAt));
            db.Experts.Add(expert);
            await db.SaveChangesAsync();

            if (withLedger)
            {
                await AddLedgerRowsAsync(db, expertId);
            }
        }

        if (claimed)
        {
            owner = factory.CreateAccount(UserRole.Expert);
            factory.SetOwner(expertId, owner.Id);
        }

        return new World(expertId, owner, email, collectedAt);
    }

    private static async Task AddLedgerRowsAsync(AppDbContext db, Guid expertId)
    {
        var job = new ScoringJob
        {
            Id = Guid.NewGuid(), JobDescription = "A job", State = ScoringJobState.Completed,
            ChunkSize = 10, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        job.Candidates.Add(new ScoringJobCandidate
        {
            Id = Guid.NewGuid(), ExpertId = expertId, Name = "Retained Person", Title = "Engineer",
            Digest = "A digest", Status = ScoringCandidateStatus.Scored, Score = 60,
        });
        db.ScoringJobs.Add(job);

        var proposalId = Guid.NewGuid();
        var proposal = new StaffingProposal
        {
            Id = proposalId, JobDescription = "A job", Status = StaffingProposalStatus.Approved,
            RecommendedExpertId = expertId, CreatedAt = DateTimeOffset.UtcNow,
            // Concatenated rather than a raw string: the document ends in a run of closing braces
            // that a raw interpolated literal reads as its own.
            PackageJson = "{\"report\":{\"candidates\":[{\"expertId\":\""
                + expertId + "\",\"name\":\"Retained Person\"}]}}",
        };
        proposal.Candidates.Add(new StaffingProposalCandidate
        {
            Id = Guid.NewGuid(), ProposalId = proposalId, ExpertId = expertId,
            Name = "Retained Person", Title = "Engineer", Rank = 1, MatchScore = 60,
            Rationale = "Plausible.",
        });
        db.StaffingProposals.Add(proposal);

        await db.SaveChangesAsync();
    }

    /// <summary>What is left behind after somebody goes — the shape the two triggers must agree on.</summary>
    private async Task<object> ResidueAsync(World world)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidate = await db.StaffingProposalCandidates.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ExpertId == world.ExpertId);

        return new
        {
            ExpertRows = await db.Experts.CountAsync(e => e.Id == world.ExpertId),
            UserRows = world.OwnerId is { } id ? await db.Users.CountAsync(u => u.Id == id) : 0,
            Records = await db.ProcessingRecords.CountAsync(r => r.ExpertId == world.ExpertId),
            Chunks = await db.ExpertSearchChunks.CountAsync(c => c.ExpertId == world.ExpertId),
            ScanRows = await db.ScoringJobCandidates.CountAsync(c => c.ExpertId == world.ExpertId),
            ProposalRowKept = candidate is not null,
            ProposalName = candidate?.Name,
            ProposalRationale = candidate?.Rationale,
            ProposalScoreKept = candidate?.MatchScore,
        };
    }

    /// <summary>Runs one pass with the clock set to a chosen moment. The sweep only asks the clock
    /// for "now", so a fixed provider is enough and the whole pass is instant.</summary>
    private async Task<RetentionSweepResult> SweepAtAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var erasure = scope.ServiceProvider.GetRequiredService<IRetentionErasure>();

        var sweep = new RetentionSweep(db, erasure, new FixedClock(now));
        return await sweep.RunOnceAsync();
    }

    /// <summary>Writes through the MCP-shaped path: unrestricted ownership, exactly as an agent's
    /// caller resolves, so the interceptor sees what it would see in that host.</summary>
    private async Task ScoreLikeAnAgentAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new ScoringJob
        {
            Id = Guid.NewGuid(), JobDescription = "A job", State = ScoringJobState.Completed,
            ChunkSize = 10, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        job.Candidates.Add(new ScoringJobCandidate
        {
            Id = Guid.NewGuid(), ExpertId = expertId, Name = "Scored", Title = "Engineer",
            Digest = "A digest", Status = ScoringCandidateStatus.Scored, Score = 80,
        });
        db.ScoringJobs.Add(job);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedFabricatedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            Status = ExpertStatus.Active,
            FirstName = "Demo",
            LastName = "Brightforge",
            Title = "Engineer",
            Email = $"demo-{Guid.NewGuid():N}@demo.example.com",
        };
        expert.ProcessingRecords.Add(ProcessingRecord.For(
            expert.Id, 1, ProcessingOrigin.StaffCreated, null,
            "Seeded onto the bench as demo data.", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    private void SetControlWord(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Id == userId).ControlWordHash =
            new ExpertToJob.Web.Auth.ControlWordHasher().Hash("correct-horse-battery-staple");
        db.SaveChanges();
    }

    private async Task SetActivityAsync(Guid expertId, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Experts.SingleAsync(e => e.Id == expertId)).LastActivityAt = at;
        await db.SaveChangesAsync();
    }

    private async Task<DateTimeOffset?> LastActivityAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Experts.AsNoTracking()
            .Where(e => e.Id == expertId).Select(e => e.LastActivityAt).SingleAsync();
    }

    private async Task<bool> ExistsAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Experts.AnyAsync(e => e.Id == expertId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
