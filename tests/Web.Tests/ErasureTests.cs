using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// Erasure against real Postgres (P1T-186). Cascades and jsonb are the entire subject here, and EF
/// InMemory has neither — a completeness test on that provider would assert that nothing it cannot
/// see is still there.
///
/// <para>What is being proven is one sentence: after somebody deletes themselves, nothing personal
/// survives in any declared store, and the rows that do survive are the ones a human decided
/// something on, hollowed out.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class ErasureTests(WebApiFactory factory)
{
    private const string ControlWord = "correct-horse-battery-staple";

    /// <summary>
    /// The completeness assertion, read off the declaration rather than a list in this file — so a
    /// store added to the declaration tomorrow is checked here without anybody editing the test.
    /// </summary>
    [Fact]
    public async Task After_erasure_nothing_personal_survives_in_any_declared_store()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        var response = await world.Client.PostAsJsonAsync(
            "/api/me/account/erase", new { controlWord = ControlWord });
        var result = await response.ReadOkAsync<ErasureResult>();

        result.ExpertId.Should().Be(world.ExpertId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Deleted outright — the row, its children two hops down, the chunks, the basis history,
        // the claim trail, the account and its devices.
        (await db.Experts.CountAsync(e => e.Id == world.ExpertId)).Should().Be(0);
        (await db.Users.CountAsync(u => u.Id == world.UserId)).Should().Be(0);
        (await db.SpokenLanguages.CountAsync(l => l.ExpertId == world.ExpertId)).Should().Be(0);
        (await db.Experiences.CountAsync(x => x.ExpertId == world.ExpertId)).Should().Be(0);
        (await db.Achievements.CountAsync(a => a.ExperienceId == world.ExperienceId)).Should().Be(0);
        (await db.Qualifications.CountAsync(q => q.ExpertId == world.ExpertId)).Should().Be(0);
        (await db.ProcessingRecords.CountAsync(r => r.ExpertId == world.ExpertId)).Should().Be(0);
        (await db.PendingClaims.CountAsync(c => c.ClaimantUserId == world.UserId)).Should().Be(0);
        (await db.PasskeyCredentials.CountAsync(p => p.UserId == world.UserId)).Should().Be(0);
        (await db.AgentUsages.CountAsync(u => u.UserId == world.UserId)).Should().Be(0);
        (await db.ScoringJobCandidates.CountAsync(c => c.ExpertId == world.ExpertId)).Should().Be(0);

        // Survives, hollowed out: a human decided on this one.
        var candidate = await db.StaffingProposalCandidates.AsNoTracking()
            .SingleAsync(c => c.ExpertId == world.ExpertId);
        candidate.Name.Should().BeEmpty();
        candidate.Title.Should().BeEmpty();
        candidate.Rationale.Should().BeEmpty();
        candidate.MatchScore.Should().Be(88, "the decision's own facts are not personal data");
        candidate.ExpertId.Should().Be(world.ExpertId,
            "kept on purpose — this is pseudonymisation under Art. 18 restriction, not anonymisation");

        // And the free text nowhere: the person's own words, searched for across every declared
        // store's text columns at once.
        await AssertNoTraceOfAsync(db, world);
    }

    /// <summary>
    /// The chunk store is the one the pause deliberately keeps and erasure deliberately destroys, so
    /// the two rules meet here. The embedding matters as much as the text: a vector *of* somebody's
    /// CV is derived personal data, and deleting the content while keeping the vector would be a
    /// scrub that left the interesting half behind.
    /// </summary>
    [Fact]
    public async Task Erasure_takes_the_search_chunks_and_their_embeddings()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExpertSearchChunks.Add(new ExpertSearchChunk
            {
                Id = Guid.NewGuid(),
                ExpertId = world.ExpertId,
                SourceType = SearchChunkSource.Summary,
                SourceId = world.ExpertId,
                Content = world.Fingerprint,
                ContentHash = "hash",
                Embedding = new Pgvector.Vector(new float[1536]),
                Model = "test",
                EmbeddedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var after = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await after.ExpertSearchChunks.CountAsync(c => c.ExpertId == world.ExpertId))
            .Should().Be(0, "chunks and their vectors go by cascade — asserted because a future "
                            + "migration could drop that cascade and nothing else would notice");
    }

    /// <summary>The proposal is a decision record, so the envelope survives and the report stops
    /// saying anything about the person. The typed half — that the document still deserializes and
    /// the approver view still renders — is asserted in <c>Agents.Tests/HandoffPackageScrubTests</c>,
    /// where the real record types live.</summary>
    [Fact]
    public async Task The_handoff_package_survives_with_the_person_taken_out_of_it()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        var result = await (await world.Client.PostAsJsonAsync(
            "/api/me/account/erase", new { controlWord = ControlWord })).ReadOkAsync<ErasureResult>();

        result.PackagesRewritten.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proposal = await db.StaffingProposals.AsNoTracking().SingleAsync(p => p.Id == world.ProposalId);

        proposal.PackageJson.Should().NotBeNullOrWhiteSpace("the decision record is not deleted");
        proposal.PackageJson.Should().NotContain(world.Fingerprint);
        proposal.PackageJson.Should().Contain("\"jobDescription\"",
            "the run's own inputs are not personal data and must survive");
    }

    // ---- The gate --------------------------------------------------------------------------------

    [Fact]
    public async Task A_wrong_control_word_erases_nothing()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        var response = await world.Client.PostAsJsonAsync(
            "/api/me/account/erase", new { controlWord = "not-the-control-word" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Experts.CountAsync(e => e.Id == world.ExpertId)).Should().Be(1);
        (await db.Users.CountAsync(u => u.Id == world.UserId)).Should().Be(1);
    }

    [Fact]
    public async Task No_route_exists_for_erasing_somebody_else()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());

        foreach (var path in new[]
                 {
                     $"/api/me/account/erase/{row.Id}",
                     $"/api/experts/{row.Id}/erase",
                     $"/api/users/{row.Id}/erase",
                 })
        {
            (await staff.PostAsJsonAsync(path, new { controlWord = ControlWord })).StatusCode
                .Should().Be(HttpStatusCode.NotFound, $"{path} must not exist");
        }
    }

    // ---- Afterwards ------------------------------------------------------------------------------

    /// <summary>
    /// The session dies with the account, on both hosts, because both re-read the account on every
    /// request and it is no longer there. Without this the person would keep working for up to a
    /// token lifetime after we told them their data was gone.
    /// </summary>
    [Fact]
    public async Task The_session_stops_working_the_moment_the_account_goes()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        (await world.Client.GetAsync("/api/me/visibility")).StatusCode
            .Should().Be(HttpStatusCode.OK, "the session works before");

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        (await world.Client.GetAsync("/api/me/visibility")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "and not after");
    }

    /// <summary>
    /// The payoff for a hard delete with no tombstone: coming back needs no design at all. The same
    /// address registers a brand-new Expert with a new id, and nothing reaches back to the old one.
    /// </summary>
    [Fact]
    public async Task Registering_again_with_the_same_address_yields_a_clean_new_record()
    {
        var world = await GivenAFullyPopulatedPersonAsync();
        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        // Registration's own matching decides this (P1T-184): nothing matches the address any more,
        // so the returning person gets a fresh row owned on the spot.
        var returning = factory.CreateAccount(UserRole.Expert);
        SetEmail(returning.Id, world.Email);
        var binding = await BindOnRegistrationAsync(returning.Id, world.Email);

        binding.Outcome.Should().Be(Application.Claims.RegistrationBinding.OwnsNewRow);
        binding.ExpertId.Should().NotBe(world.ExpertId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.Experts.AsNoTracking().SingleAsync(e => e.Id == binding.ExpertId);
        fresh.OwnerUserId.Should().Be(returning.Id);
        (await db.ProcessingRecords.CountAsync(r => r.ExpertId == world.ExpertId)).Should().Be(0);
    }

    // ---- Fixture ------------------------------------------------------------------------------------

    private sealed record World(
        HttpClient Client,
        Guid UserId,
        Guid ExpertId,
        Guid ExperienceId,
        Guid ProposalId,
        string Email,
        string Fingerprint);

    /// <summary>
    /// One person with something in every declared store: a full CV, a lawful-basis history, a
    /// passkey, agent usage, a resolved claim, a scan candidate carrying their career digest, and a
    /// proposal a Service Manager decided on. Their name is a unique nonsense string
    /// (<c>Fingerprint</c>) so "no trace survives" can be asserted by searching for it.
    /// </summary>
    private async Task<World> GivenAFullyPopulatedPersonAsync()
    {
        var fingerprint = $"Zarquon{Guid.NewGuid():N}";
        var email = ApiClientExtensions.UniqueEmail("erasure");

        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(
            ApiClientExtensions.NewExpert(firstName: fingerprint, lastName: "Erasable", email: email));

        var (client, account) = factory.CreateExpertClientOwning(expert.Id);
        var experienceId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            SetControlWord(db, account.Id, email);

            db.SpokenLanguages.Add(new SpokenLanguage
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Language = fingerprint, Level = LanguageLevel.Native,
            });
            db.Qualifications.Add(new Qualification
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Type = QualificationType.Degree,
                Name = fingerprint, Institution = fingerprint,
            });
            db.Experiences.Add(new Experience
            {
                Id = experienceId,
                ExpertId = expert.Id,
                Company = fingerprint,
                Title = "Engineer",
                StartDate = new DateOnly(2020, 1, 1),
                Achievements = { new Achievement { Id = Guid.NewGuid(), Text = fingerprint, Order = 1 } },
            });
            db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(), UserId = account.Id,
                // Unique per person: the credential id carries its own unique index, and the suite
                // shares one database.
                CredentialId = Guid.NewGuid().ToByteArray(), PublicKey = [4, 5, 6],
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.PendingClaims.Add(new PendingClaim
            {
                Id = Guid.NewGuid(), ClaimantUserId = account.Id, ClaimantEmail = email,
                ExpertId = expert.Id, MatchCount = 1, State = ClaimState.Approved,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            var job = new ScoringJob
            {
                Id = Guid.NewGuid(),
                JobDescription = "A job",
                State = ScoringJobState.Completed,
                ChunkSize = 10,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            job.Candidates.Add(new ScoringJobCandidate
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Name = fingerprint, Title = "Engineer",
                Digest = $"Career digest for {fingerprint}", Status = ScoringCandidateStatus.Scored,
                Score = 70, Rationale = $"{fingerprint} is plausible.",
            });
            db.ScoringJobs.Add(job);

            var proposal = new StaffingProposal
            {
                Id = proposalId,
                JobDescription = "A job",
                Status = StaffingProposalStatus.Approved,
                RecommendedExpertId = expert.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                PackageJson = PackageJsonFor(expert.Id, fingerprint),
            };
            proposal.Candidates.Add(new StaffingProposalCandidate
            {
                Id = Guid.NewGuid(), ProposalId = proposalId, ExpertId = expert.Id,
                Name = fingerprint, Title = "Engineer", Rank = 1, MatchScore = 88,
                Rationale = $"{fingerprint} matched well.",
            });
            db.StaffingProposals.Add(proposal);

            await db.SaveChangesAsync();
        }

        return new World(client, account.Id, expert.Id, experienceId, proposalId, email, fingerprint);
    }

    /// <summary>A handoff document in the shape the Agents host writes, with the person in all six
    /// places the scrub is responsible for.</summary>
    private static string PackageJsonFor(Guid expertId, string fingerprint) =>
        $$"""
        {
          "inputs": { "jobDescription": "A job" },
          "report": {
            "candidates": [
              {
                "expertId": "{{expertId}}",
                "name": "{{fingerprint}}",
                "title": "Engineer",
                "rationale": "{{fingerprint}} matched well.",
                "match": { "answer": "{{fingerprint}} has the experience." },
                "shortlist": { "requirements": [ { "text": "React", "snippet": "{{fingerprint}} built it." } ] }
              }
            ],
            "recommendation": { "expertId": "{{expertId}}", "narrative": "Pick {{fingerprint}}." }
          },
          "provenance": { "startedAt": "2026-09-01T00:00:00Z" },
          "slices": [],
          "degradations": []
        }
        """;

    /// <summary>
    /// The one text sweep: every string column of every declared store, searched for the person's
    /// unique name. This is the assertion that survives somebody adding a column — a targeted check
    /// per field would not.
    /// </summary>
    private static async Task AssertNoTraceOfAsync(AppDbContext db, World world)
    {
        var found = new List<string>();

        foreach (var store in PersonalDataDeclaration.Erased)
        {
            var entity = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == store.Entity);
            var table = entity.GetTableName();
            var columns = entity.GetProperties()
                .Where(p => p.ClrType == typeof(string))
                .Select(p => p.GetColumnName())
                .ToList();

            foreach (var column in columns)
            {
                // Raw SQL because the point is to read the table as it actually stands, not as an
                // entity graph EF would happily project around.
                // ::text because PackageJson is jsonb, and jsonb has no LIKE. Casting keeps the
                // sweep uniform: every string-shaped column is read as the text it stores.
                var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM \"{table}\" WHERE \"{column}\"::text LIKE {{0}}";
                var hits = await db.Database
                    .SqlQueryRaw<int>(sql, $"%{world.Fingerprint}%")
                    .SingleAsync();

                if (hits > 0)
                {
                    found.Add($"{table}.{column} ({hits})");
                }
            }
        }

        found.Should().BeEmpty(
            "the person's own words must not survive anywhere the declaration says they were: "
            + string.Join(", ", found));
    }

    private static void SetControlWord(AppDbContext db, Guid userId, string email)
    {
        var hasher = new ExpertToJob.Web.Auth.ControlWordHasher();
        var user = db.Users.Single(u => u.Id == userId);
        user.ControlWordHash = hasher.Hash(ControlWord);
        user.Email = email;
        db.SaveChanges();
    }

    private void SetEmail(Guid userId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Id == userId).Email = email;
        db.SaveChanges();
    }

    private async Task<Application.Claims.RegistrationBindingDto> BindOnRegistrationAsync(
        Guid userId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var claims = scope.ServiceProvider.GetRequiredService<Application.Claims.IClaimService>();
        return await claims.BindOnRegistrationAsync(userId, email, TransparencyNotice.CurrentVersion);
    }
}
