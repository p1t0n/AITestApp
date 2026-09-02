using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Application.Claims;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The two transparency surfaces (P1T-187), against real Postgres because the derived data is
/// gathered across four tables and the labelling turns on the append-only basis history.
///
/// <para>The pair of assertions that carry this slice run in opposite directions: everything
/// erasure would destroy has to be <b>reachable</b> in the access view, and the data software
/// worked out about somebody has to be <b>absent</b> from the Art. 20 copy.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class TransparencyTests(WebApiFactory factory)
{
    // ---- Art. 15: what we hold ------------------------------------------------------------------

    [Fact]
    public async Task The_access_view_carries_every_item_article_15_owes()
    {
        var world = await GivenAScoredPersonAsync();

        var view = await (await world.Client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();

        view.Purposes.Should().NotBeEmpty();
        view.DataCategories.Should().NotBeEmpty();
        view.Recipients.Should().NotBeEmpty();
        view.Retention.Should().NotBeNullOrWhiteSpace();
        view.Rights.Should().NotBeEmpty();
        view.ComplaintRight.Should().Contain("supervisory authority");
        view.Art22Logic.Should().NotBeNullOrWhiteSpace();
        view.History.Should().NotBeEmpty("the basis history is where 'why' and 'since when' come from");
        view.Record.FullName.Should().Contain(world.Fingerprint);
    }

    /// <summary>
    /// The one item here that is new information rather than a restatement: until this slice the
    /// service named its model provider to nobody, while sending every CV to it.
    /// </summary>
    [Fact]
    public async Task The_access_view_names_the_model_provider()
    {
        var world = await GivenAScoredPersonAsync();

        var view = await (await world.Client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();

        view.Recipients.Should().Contain(r => r.Recipient.Contains("Google"));
        view.Recipients.Single(r => r.Recipient.Contains("Google")).Why
            .Should().Contain("outside this company");
    }

    /// <summary>
    /// Art. 15(1)(g): the source, and only where the data did not come from the person. A record
    /// somebody registered has no source to disclose; one a Service Manager typed in does — and this
    /// service could never have told them at the time.
    /// </summary>
    [Fact]
    public async Task A_staff_created_record_is_told_where_it_came_from()
    {
        var world = await GivenAScoredPersonAsync();

        var view = await (await world.Client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();
        view.Origin.Should().Be(ProcessingOrigin.StaffCreated);
        view.Source.Should().Contain("A Service Manager");

        await ApproveIntoContractNecessityAsync(world.ExpertId);

        var afterClaim = await (await world.Client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();
        afterClaim.Origin.Should().Be(ProcessingOrigin.SelfRegistered);
        afterClaim.Source.Should().BeNull("nothing to disclose once the record is theirs by their own act");
    }

    // ---- The two directions ----------------------------------------------------------------------

    /// <summary>
    /// Derived data is owed under access (EDPB GL 01/2022 §§97–99) and excluded from portability, so
    /// the filter has to run in one direction and not the other. Proven both ways in one test,
    /// because a change that broke the pair would otherwise pass half of it.
    ///
    /// <para>The consequence this bakes in, deliberately: the rationale a model writes about somebody
    /// is shown to them. If we would not show it, it should not have been written.</para>
    /// </summary>
    [Fact]
    public async Task Derived_data_is_in_the_access_view_and_out_of_the_export()
    {
        var world = await GivenAScoredPersonAsync();

        var view = await (await world.Client.GetAsync("/api/me/access")).ReadOkAsync<AccessViewDto>();
        var assessments = view.Derived.Assessments;

        assessments.Should().HaveCount(2, "one roster scan and one staffing proposal");
        assessments.Should().Contain(a => a.Rationale != null && a.Rationale.Contains(world.Fingerprint));
        assessments.Should().Contain(a => a.Digest != null && a.Digest.Contains(world.Fingerprint));
        assessments.Should().Contain(a => a.MatchAnswer != null && a.MatchAnswer.Contains(world.Fingerprint));
        assessments.Should().Contain(a => a.Score == 70).And.Contain(a => a.Score == 88);

        var exportJson = await (await world.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync();

        exportJson.Should().Contain(world.Fingerprint, "their own record is the whole point of the copy");
        foreach (var derived in new[] { world.Rationale, world.Digest, world.MatchAnswer })
        {
            exportJson.Should().NotContain(derived,
                "Art. 20 covers what the person provided, not what software concluded about them");
        }

        using var parsed = JsonDocument.Parse(exportJson);
        parsed.RootElement.TryGetProperty("derived", out _).Should().BeFalse();
    }

    /// <summary>
    /// The symmetry the shared declaration makes provable rather than aspirational: everything
    /// erasure would destroy is reachable by the person while it exists. Asserted against the
    /// <em>access view</em> and not the Art. 20 copy — the copy deliberately excludes derived data,
    /// and the scrub deliberately reaches it, so the two would contradict.
    /// </summary>
    [Fact]
    public async Task Every_store_the_scrub_reaches_is_visible_in_the_access_view()
    {
        var world = await GivenAScoredPersonAsync();

        var raw = await (await world.Client.GetAsync("/api/me/access")).Content.ReadAsStringAsync();

        var unreachable = new List<string>();
        foreach (var store in PersonalDataDeclaration.Erased.Where(s => s.PersonalFields.Count > 0))
        {
            // One value actually stored in this person's copy of that store, so "reachable" means
            // the data is there rather than the word being somewhere in the JSON.
            var sample = await SampleValueAsync(store.Entity, world);
            if (sample is not null && !raw.Contains(sample, StringComparison.Ordinal))
            {
                unreachable.Add($"{store.Entity} ({sample})");
            }
        }

        unreachable.Should().BeEmpty(
            "a store erasure destroys is a store the person is entitled to see while it exists — "
            + "both sides read PersonalDataDeclaration, so this cannot drift: " + string.Join(", ", unreachable));
    }

    // ---- Art. 20: the copy, and what it is called -------------------------------------------------

    /// <summary>
    /// Art. 20 is owed to a 6(1)(b) record and not to a legitimate-interest one. We hand over the
    /// same file either way and change only the word for it — and the word has to keep telling the
    /// truth by itself when an approved claim moves the basis.
    /// </summary>
    [Fact]
    public async Task The_export_is_a_courtesy_on_legitimate_interest_and_a_right_after_a_claim()
    {
        var world = await GivenAScoredPersonAsync();

        var courtesy = await ExportAsync(world.Client);
        courtesy.Entitlement.Should().Be(ExportEntitlement.Courtesy);
        courtesy.EntitlementNote.Should().Contain("courtesy rather than as a right");

        await ApproveIntoContractNecessityAsync(world.ExpertId);

        var right = await ExportAsync(world.Client);
        right.Entitlement.Should().Be(ExportEntitlement.Right);
        right.EntitlementNote.Should().Contain("Art. 20");

        // The same file, differently labelled — that is the whole design.
        Payload(courtesy).Should().BeEquivalentTo(Payload(right),
            "withholding or trimming the payload would be a basis check whose only job is to deny "
            + "something we are happy to give");

        // The history is the one thing that legitimately differs, and only by growing: the claim
        // approval is itself a new fact appended to it, and the earlier rows are untouched.
        right.History.Should().HaveCount(courtesy.History.Count + 1);
        right.History.Take(courtesy.History.Count).Should().BeEquivalentTo(courtesy.History);
    }

    [Fact]
    public async Task The_export_downloads_as_a_json_file_in_one_request()
    {
        var world = await GivenAScoredPersonAsync();

        var response = await world.Client.GetAsync("/api/me/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "synchronous: no queue, no ready-shortly state");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(world.ExpertId.ToString());
    }

    // ---- On behalf --------------------------------------------------------------------------------

    /// <summary>
    /// A staff member extracting somebody's complete file leaves a trace. What makes this different
    /// from the per-row read log that was rejected: it records one deliberate act of taking a copy,
    /// it is a fact about the Service Manager, and merely looking at a record writes nothing.
    /// </summary>
    [Fact]
    public async Task A_service_manager_export_writes_its_own_record_and_a_self_export_does_not()
    {
        var world = await GivenAScoredPersonAsync();
        var (staff, staffAccount) = factory.CreateClientFor(UserRole.ServiceManager);
        using var _staff = staff;

        await world.Client.GetAsync("/api/me/export");
        (await ExportRecordsForAsync(world.ExpertId)).Should().BeEmpty(
            "reading your own data is not an event worth a row about you");

        var response = await staff.PostAsJsonAsync($"/api/experts/{world.ExpertId}/export", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var records = await ExportRecordsForAsync(world.ExpertId);
        records.Should().ContainSingle();
        records[0].ExportedByUserId.Should().Be(staffAccount.Id, "the row names the staff member");

        // Looking at the record, rather than exporting it, still writes nothing.
        await staff.GetAsync($"/api/experts/{world.ExpertId}");
        await staff.GetAsync($"/api/experts/{world.ExpertId}/cv");
        (await ExportRecordsForAsync(world.ExpertId)).Should().HaveCount(1, "this is not a read log");
    }

    [Fact]
    public async Task An_expert_cannot_export_somebody_elses_record()
    {
        var world = await GivenAScoredPersonAsync();
        using var stranger = factory.CreateExpertClient();

        (await stranger.PostAsJsonAsync($"/api/experts/{world.ExpertId}/export", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And their own surfaces answer for the record they own, which is none.
        (await stranger.GetAsync("/api/me/access")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync("/api/me/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Fixture ------------------------------------------------------------------------------------

    private sealed record World(
        HttpClient Client, Guid ExpertId, string Fingerprint,
        string Rationale, string Digest, string MatchAnswer);

    /// <summary>
    /// One person who has been through the machinery: a staff-created record on legitimate interest,
    /// scored by a roster scan and named in a decided staffing proposal. Everything derived about
    /// them carries their unique nonsense name, so "in the access view" and "not in the export" can
    /// both be asserted by looking for it.
    /// </summary>
    private async Task<World> GivenAScoredPersonAsync()
    {
        var fingerprint = $"Quilliam{Guid.NewGuid():N}";
        var rationale = $"{fingerprint} reads as a strong fit on payments.";
        var digest = $"Career digest for {fingerprint}: payments, platforms.";
        var matchAnswer = $"{fingerprint} scores 88 because of the settlement work.";

        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(
            ApiClientExtensions.NewExpert(firstName: fingerprint, lastName: "Quantrell"));
        var (client, _) = factory.CreateExpertClientOwning(expert.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.SpokenLanguages.Add(new SpokenLanguage
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Language = fingerprint,
                Level = LanguageLevel.Native,
            });
            db.Qualifications.Add(new Qualification
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Type = QualificationType.Degree,
                Name = fingerprint, Institution = fingerprint,
            });
            db.Experiences.Add(new Experience
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Company = fingerprint, Title = "Engineer",
                StartDate = new DateOnly(2020, 1, 1),
                Achievements = { new Achievement { Id = Guid.NewGuid(), Text = fingerprint, Order = 1 } },
            });

            var job = new ScoringJob
            {
                Id = Guid.NewGuid(), JobDescription = "Payments platform", State = ScoringJobState.Completed,
                ChunkSize = 10, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            job.Candidates.Add(new ScoringJobCandidate
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Name = fingerprint, Title = "Engineer",
                Digest = digest, Status = ScoringCandidateStatus.Scored, Score = 70, Band = "fair",
                Rationale = rationale,
            });
            db.ScoringJobs.Add(job);

            var proposalId = Guid.NewGuid();
            var proposal = new StaffingProposal
            {
                Id = proposalId, JobDescription = "Payments platform",
                Status = StaffingProposalStatus.Approved, RecommendedExpertId = expert.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                PackageJson = $$"""
                {
                  "inputs": { "jobDescription": "Payments platform" },
                  "report": {
                    "candidates": [
                      { "expertId": "{{expert.Id}}", "name": "{{fingerprint}}",
                        "match": { "answer": "{{matchAnswer}}" } }
                    ]
                  }
                }
                """,
            };
            proposal.Candidates.Add(new StaffingProposalCandidate
            {
                Id = Guid.NewGuid(), ProposalId = proposalId, ExpertId = expert.Id,
                Name = fingerprint, Title = "Engineer", Rank = 1, MatchScore = 88, MatchBand = "strong",
                Rationale = rationale,
            });
            db.StaffingProposals.Add(proposal);

            await db.SaveChangesAsync();
        }

        return new World(client, expert.Id, fingerprint, rationale, digest, matchAnswer);
    }

    /// <summary>Moves the record onto 6(1)(b) the way an approved claim does — by appending, never
    /// by rewriting (P1T-183).</summary>
    private async Task ApproveIntoContractNecessityAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sequence = await db.ProcessingRecords.CountAsync(r => r.ExpertId == expertId);

        db.ProcessingRecords.Add(ProcessingRecord.For(
            expertId, sequence + 1, ProcessingOrigin.SelfRegistered, TransparencyNotice.CurrentVersion,
            "Claim approved.", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>One value this person actually has stored in the named store, or null when the store
    /// holds nothing of theirs in this fixture.</summary>
    private async Task<string?> SampleValueAsync(string entity, World world) => entity switch
    {
        "Expert" => world.Fingerprint,
        "SpokenLanguage" => world.Fingerprint,
        "Qualification" => world.Fingerprint,
        "Experience" => world.Fingerprint,
        "Achievement" => world.Fingerprint,
        "ProcessingRecord" => (await CurrentReasonAsync(world.ExpertId)),
        "ScoringJobCandidate" => world.Digest,
        "StaffingProposalCandidate" => world.Rationale,
        "StaffingProposal" => world.MatchAnswer,
        // Stores holding no free text of the person's own: the account, its devices, the claim
        // trail, the chunk store (derived from text already listed above).
        _ => null,
    };

    private async Task<string> CurrentReasonAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expertId)
            .OrderByDescending(r => r.Sequence)
            .Select(r => r.Reason)
            .FirstAsync();
    }

    private async Task<List<DataExportRecord>> ExportRecordsForAsync(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DataExportRecords.AsNoTracking()
            .Where(r => r.ExpertId == expertId).ToListAsync();
    }

    private static async Task<DataExportDto> ExportAsync(HttpClient client) =>
        JsonSerializer.Deserialize<DataExportDto>(
            await (await client.GetAsync("/api/me/export")).Content.ReadAsStringAsync(),
            WebApiFactory.Json)!;

    /// <summary>The export minus the three things that are allowed to differ across a basis change:
    /// the label, the moment it was taken, and the basis history, which grows by the transition
    /// itself. What is left is the person's own data, and that has to be identical.</summary>
    private static object Payload(DataExportDto export) =>
        new { export.ExpertId, export.Record };
}
