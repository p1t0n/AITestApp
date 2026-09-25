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

    // ---- Roster Q&A transcripts (EXP-32) ---------------------------------------------------------

    /// <summary>
    /// The scrub reaches a turn two ways and has to leave the third alone. The touched id is what
    /// catches a turn that never typed the person's name; the name is what catches a turn no tool
    /// result ever pointed at them from. A turn about somebody else is neither, and survives whole
    /// — which is the assertion that would catch a scrub written as "empty this conversation".
    /// </summary>
    [Fact]
    public async Task Erasure_empties_the_turns_that_touched_or_named_them_and_no_others()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var touched = await db.RosterQaTurns.AsNoTracking().SingleAsync(t => t.Id == world.Qa.TouchedTurnId);
        touched.QuestionText.Should().BeEmpty("their id came back in a tool result during this turn");
        touched.AnswerText.Should().BeEmpty();
        touched.State.Should().Be(RosterQaTurnState.Removed);

        var named = await db.RosterQaTurns.AsNoTracking().SingleAsync(t => t.Id == world.Qa.NamedTurnId);
        named.QuestionText.Should().BeEmpty("the asker typed their full name, and no id reached it");
        named.AnswerText.Should().BeEmpty();
        named.State.Should().Be(RosterQaTurnState.Removed);

        var lowerCase = await db.RosterQaTurns.AsNoTracking()
            .SingleAsync(t => t.Id == world.Qa.LowerCaseTurnId);
        lowerCase.AnswerText.Should().BeEmpty("the name match is case-insensitive");
        lowerCase.State.Should().Be(RosterQaTurnState.Removed);

        var unrelated = await db.RosterQaTurns.AsNoTracking()
            .SingleAsync(t => t.Id == world.Qa.UnrelatedTurnId);
        unrelated.QuestionText.Should().Be("And who else could cover it?",
            "this turn touched another Expert and named nobody — erasing one person does not empty "
            + "a third party's conversation");
        unrelated.AnswerText.Should().Be("One other engineer could.");
        unrelated.State.Should().Be(RosterQaTurnState.Ok);

        (await db.RosterQaTurnExperts.CountAsync(t => t.ExpertId == world.ExpertId))
            .Should().Be(0, "the ids are a reference to somebody we no longer hold");
        (await db.RosterQaTurnExperts.CountAsync(t => t.ExpertId == world.Qa.OtherExpertId))
            .Should().Be(1, "and the other Expert's are untouched");
    }

    /// <summary>
    /// The conversation is still the asker's. Its shape — when they asked, how many turns, on which
    /// model — is their own data and not the erased person's, so nothing but the text goes.
    /// </summary>
    [Fact]
    public async Task The_scrubbed_conversation_keeps_its_other_turns_and_its_timestamps()
    {
        var world = await GivenAFullyPopulatedPersonAsync();

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var conversation = await db.RosterQaConversations.AsNoTracking()
            .SingleAsync(c => c.Id == world.Qa.ConversationId);
        conversation.UserId.Should().Be(world.Qa.AskerUserId);
        conversation.CreatedAt.Should().Be(world.Qa.CreatedAt);
        conversation.LastActiveAt.Should().Be(world.Qa.LastActiveAt);

        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == world.Qa.ConversationId))
            .Should().Be(4, "the rows stay — the scrub hollows them out, it does not delete them");

        var scrubbed = await db.RosterQaTurns.AsNoTracking()
            .SingleAsync(t => t.Id == world.Qa.TouchedTurnId);
        scrubbed.CreatedAt.Should().Be(world.Qa.CreatedAt);
        scrubbed.ModelId.Should().Be("gemini-2.5-flash", "which model answered is not their data");
    }

    /// <summary>
    /// The title is the asker's first question verbatim, so it can name the erased person outright.
    /// Both routes to retitling are asserted, because they are genuinely different rules: the name
    /// appearing in the title, and the turn the title was taken from being emptied.
    /// </summary>
    [Fact]
    public async Task A_title_that_names_them_becomes_the_dated_placeholder()
    {
        var world = await GivenAFullyPopulatedPersonAsync();
        var conversationId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 2, 17, 11, 0, 0, TimeSpan.Zero);

        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            // Nothing in this conversation touches or names them except the title itself, so the
            // title rule is the only thing that can fire here.
            var conversation = new RosterQaConversation
            {
                Id = conversationId,
                UserId = factory.CreateAccount(UserRole.User).Id,
                CreatedAt = createdAt,
                LastActiveAt = createdAt.AddMinutes(2),
                Title = $"Is {world.Fingerprint} Erasable free in May?",
            };
            conversation.Turns.Add(new RosterQaTurn
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                QuestionText = "Who is free in May?",
                AnswerText = "Somebody else is.",
                ModelId = "gemini-2.5-flash",
                Grounded = true,
                CreatedAt = createdAt,
            });
            db.RosterQaConversations.Add(conversation);
            await db.SaveChangesAsync();
        }

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conversation2 = await db2.RosterQaConversations.AsNoTracking()
            .SingleAsync(c => c.Id == conversationId);

        conversation2.Title.Should().Be("Conversation from 2026-02-17",
            "the title said their name, and the date it keeps is the conversation's own");
        (await db2.RosterQaTurns.AsNoTracking().SingleAsync(t => t.ConversationId == conversationId))
            .QuestionText.Should().Be("Who is free in May?",
                "the turn under it mentioned nobody — retitling is not a reason to empty it");
    }

    [Fact]
    public async Task A_conversation_whose_first_turn_was_scrubbed_is_retitled_too()
    {
        var world = await GivenAFullyPopulatedPersonAsync();
        var conversationId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 1, 9, 8, 0, 0, TimeSpan.Zero);

        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            // A title that names nobody, over a first turn that touched them. Nothing in the title
            // itself is a reason to rewrite it — what makes it meaningless is that the question it
            // was taken from is about to be emptied.
            var conversation = new RosterQaConversation
            {
                Id = conversationId,
                UserId = factory.CreateAccount(UserRole.User).Id,
                CreatedAt = createdAt,
                LastActiveAt = createdAt.AddMinutes(5),
                Title = "Who is free in May?",
            };
            conversation.Turns.Add(new RosterQaTurn
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                QuestionText = "Who is free in May?",
                AnswerText = "One person is.",
                ModelId = "gemini-2.5-flash",
                Grounded = true,
                CreatedAt = createdAt,
                TouchedExperts = { new RosterQaTurnExpert { ExpertId = world.ExpertId } },
            });
            db.RosterQaConversations.Add(conversation);
            await db.SaveChangesAsync();
        }

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var after = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await after.RosterQaConversations.AsNoTracking().SingleAsync(c => c.Id == conversationId))
            .Title.Should().Be("Conversation from 2026-01-09");
    }

    /// <summary>
    /// The owner's own conversations need no code at all: the <c>UserId</c> foreign key takes them,
    /// their turns and their touched rows. Asserted because a future migration could drop that
    /// cascade and every other test here would still pass.
    /// </summary>
    [Fact]
    public async Task Erasing_the_account_cascades_its_own_conversations()
    {
        var world = await GivenAFullyPopulatedPersonAsync();
        var conversationId = Guid.NewGuid();
        var turnId = Guid.NewGuid();

        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var conversation = new RosterQaConversation
            {
                Id = conversationId,
                UserId = world.UserId,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                Title = "My own question",
            };
            conversation.Turns.Add(new RosterQaTurn
            {
                Id = turnId,
                ConversationId = conversationId,
                QuestionText = "Who builds payments?",
                AnswerText = "Three people do.",
                ModelId = "gemini-2.5-flash",
                Grounded = true,
                CreatedAt = DateTimeOffset.UtcNow,
                TouchedExperts = { new RosterQaTurnExpert { ExpertId = world.Qa.OtherExpertId } },
            });
            db.RosterQaConversations.Add(conversation);
            await db.SaveChangesAsync();
        }

        await world.Client.PostAsJsonAsync("/api/me/account/erase", new { controlWord = ControlWord });

        using var scope = factory.Services.CreateScope();
        var after = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await after.RosterQaConversations.CountAsync(c => c.Id == conversationId)).Should().Be(0);
        (await after.RosterQaTurns.CountAsync(t => t.Id == turnId)).Should().Be(0);
        (await after.RosterQaTurnExperts.CountAsync(t => t.TurnId == turnId)).Should().Be(0);
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
        var returning = factory.CreateAccount(UserRole.User);
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
        string Fingerprint,
        RosterQa Qa);

    /// <summary>
    /// One Roster Q&A conversation, asked by <b>somebody else</b> about this person (EXP-32). Owned
    /// by another account on purpose: the conversation the erased person asked themselves goes by
    /// cascade and proves nothing about the scrub, and the interesting case is the transcript that
    /// survives because it is a third party's own data.
    /// </summary>
    private sealed record RosterQa(
        Guid AskerUserId,
        Guid ConversationId,
        Guid TouchedTurnId,
        Guid NamedTurnId,
        Guid LowerCaseTurnId,
        Guid UnrelatedTurnId,
        Guid OtherExpertId,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActiveAt);

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

        var (client, account) = factory.CreateUserClientOwning(expert.Id);
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

        var qa = await GivenSomebodyElseAskedAboutThemAsync(expert.Id, fingerprint);

        return new World(
            client, account.Id, expert.Id, experienceId, proposalId, email, fingerprint, qa);
    }

    /// <summary>
    /// A stored conversation belonging to a different account, holding the three turns the scrub has
    /// to tell apart: one that only <em>touched</em> this person (their id came back in a tool
    /// result, their name is nowhere in the text), one that only <em>names</em> them (nothing
    /// touched them, the asker typed the name), and one about somebody else entirely. Its title is
    /// the first question verbatim, so it names them too.
    /// </summary>
    private async Task<RosterQa> GivenSomebodyElseAskedAboutThemAsync(Guid expertId, string fingerprint)
    {
        // First plus last, which is what the scrub matches on. The first name alone is not a match:
        // it is a real word in somebody else's sentence often enough that it would be a licence to
        // empty conversations the erased person never appeared in.
        var fullName = $"{fingerprint} Erasable";

        var asker = factory.CreateAccount(UserRole.User);
        var otherExpert = await factory.CreateAuthenticatedClient()
            .CreateExpertAsync(ApiClientExtensions.NewExpert());

        var createdAt = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var conversation = new RosterQaConversation
        {
            Id = Guid.NewGuid(),
            UserId = asker.Id,
            CreatedAt = createdAt,
            LastActiveAt = createdAt.AddMinutes(20),
            Title = $"Is {fullName} free in May?",
        };

        var touched = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            QuestionText = "Who is free in May?",
            AnswerText = "Two people are, both React engineers.",
            ModelId = "gemini-2.5-flash",
            Grounded = true,
            CreatedAt = createdAt,
            TouchedExperts = { new RosterQaTurnExpert { ExpertId = expertId } },
        };

        var named = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            QuestionText = $"What has {fullName} shipped?",
            AnswerText = $"{fullName} led two payment migrations.",
            ModelId = "gemini-2.5-flash",
            Grounded = true,
            CreatedAt = createdAt.AddMinutes(10),
        };

        // The same name in a different case, and nothing else to catch it by — so a scrub that
        // matched case-sensitively would leave this answer standing and this test would say so.
        var lowerCase = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            QuestionText = "Anyone else with that background?",
            AnswerText = $"{fullName.ToLowerInvariant()} has it too.",
            ModelId = "gemini-2.5-flash",
            Grounded = true,
            CreatedAt = createdAt.AddMinutes(15),
        };

        var unrelated = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            QuestionText = "And who else could cover it?",
            AnswerText = "One other engineer could.",
            ModelId = "gemini-2.5-pro",
            Grounded = false,
            CreatedAt = createdAt.AddMinutes(20),
            TouchedExperts = { new RosterQaTurnExpert { ExpertId = otherExpert.Id } },
        };

        conversation.Turns.AddRange([touched, named, lowerCase, unrelated]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RosterQaConversations.Add(conversation);
        await db.SaveChangesAsync();

        return new RosterQa(
            asker.Id, conversation.Id, touched.Id, named.Id, lowerCase.Id, unrelated.Id,
            otherExpert.Id, conversation.CreatedAt, conversation.LastActiveAt);
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
