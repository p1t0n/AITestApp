using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The parts of the compliance spine that are only true if the database says so (P1T-183). EF
/// InMemory has neither triggers nor check constraints, so append-only and basis-per-origin would
/// pass there while being nothing but a convention two files away from being forgotten.
///
/// <para>Everything here runs against the Postgres the host migrated, through raw SQL rather than
/// through EF — a rule EF is asked to enforce is a rule EF can be talked out of.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class ProcessingRecordDatabaseTests(WebApiFactory factory)
{
    /// <summary>
    /// The structural check the ticket asks for: an Expert with no recorded lawful basis is a
    /// compliance defect and fails the build. Over the whole live database — the dev seed, the
    /// bootstrap, and every row this suite created — because "the code path I remembered writes
    /// one" is exactly the claim that needs checking against reality.
    /// </summary>
    [Fact]
    public async Task Every_expert_row_in_the_database_has_a_recorded_lawful_basis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The seed puts three samples in; a check that passed on an empty roster would prove nothing.
        (await db.Experts.CountAsync()).Should().BeGreaterThan(0);

        var withoutBasis = await db.Experts
            .AsNoTracking()
            .Where(e => !db.ProcessingRecords.Any(r => r.ExpertId == e.Id))
            .Select(e => e.Email)
            .ToListAsync();

        withoutBasis.Should().BeEmpty(
            "a roster row whose lawful basis was never recorded is a compliance defect — it should " +
            "fail here rather than be discovered in an audit");
    }

    [Fact]
    public async Task The_seeded_roster_sits_on_legitimate_interest_with_no_notice_acknowledged()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seeded = await db.ProcessingRecords.AsNoTracking()
            .Where(r => r.Sequence == 1)
            .Take(50)
            .ToListAsync();

        seeded.Should().NotBeEmpty();
        seeded.Should().OnlyContain(r => r.Origin == ProcessingOrigin.StaffCreated);
        seeded.Should().OnlyContain(r => r.Basis == LawfulBasis.LegitimateInterest);
        seeded.Should().OnlyContain(r => r.NoticeVersion == null,
            "nobody seeded was shown anything — this is the Art. 14 population we cannot reach");
    }

    /// <summary>
    /// Append-only, enforced by the trigger. The UPDATE is raw SQL, so nothing in the Application
    /// layer is between the attempt and the refusal.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_to_rewrite_a_processing_record()
    {
        var expert = await factory.CreateAuthenticatedClient()
            .CreateExpertAsync(ApiClientExtensions.NewExpert());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var before = await db.ProcessingRecords.AsNoTracking().SingleAsync(r => r.ExpertId == expert.Id);

        var rewrite = async () => await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "ProcessingRecords"
               SET "Origin" = 'SelfRegistered', "Basis" = 'ContractNecessity'
             WHERE "Id" = {0};
            """,
            before.Id);

        await rewrite.Should().ThrowAsync<PostgresException>(
            "a lawful basis is superseded by a new row, never rewritten (EDPB GL 05/2020 §123)");

        var after = await db.ProcessingRecords.AsNoTracking().SingleAsync(r => r.Id == before.Id);
        after.Should().BeEquivalentTo(before, "the refused UPDATE must leave the row exactly as it was");
    }

    /// <summary>
    /// Basis-per-origin as database truth. This is what "no global default path exists" means: even
    /// a hand-written INSERT that skips the domain factory cannot land a row on the wrong ground.
    /// </summary>
    [Theory]
    [InlineData("SelfRegistered", "LegitimateInterest")]
    [InlineData("StaffCreated", "ContractNecessity")]
    public async Task The_database_refuses_a_basis_that_does_not_match_its_origin(string origin, string basis)
    {
        var expert = await factory.CreateAuthenticatedClient()
            .CreateExpertAsync(ApiClientExtensions.NewExpert());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var insert = async () => await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ProcessingRecords"
                ("Id", "ExpertId", "Sequence", "Origin", "Basis", "NoticeVersion", "Reason", "RecordedAt")
            VALUES (gen_random_uuid(), {0}, 99, {1}, {2}, NULL, 'Hand-written, bypassing the domain.', now());
            """,
            expert.Id, origin, basis);

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("CK_ProcessingRecords_BasisMatchesOrigin");
    }

    [Fact]
    public async Task A_row_cannot_have_two_records_at_the_same_position_in_its_history()
    {
        var expert = await factory.CreateAuthenticatedClient()
            .CreateExpertAsync(ApiClientExtensions.NewExpert());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duplicate = async () => await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ProcessingRecords"
                ("Id", "ExpertId", "Sequence", "Origin", "Basis", "NoticeVersion", "Reason", "RecordedAt")
            VALUES (gen_random_uuid(), {0}, 1, 'StaffCreated', 'LegitimateInterest', NULL, 'Second first row.', now());
            """,
            expert.Id);

        await duplicate.Should().ThrowAsync<PostgresException>(
            "'which basis is in force' must have exactly one answer");
    }

    /// <summary>
    /// Erasure still works. Deleting the row takes its history with it — a different act from
    /// rewriting the basis, and one the append-only trigger deliberately does not block (P1T-186).
    /// </summary>
    [Fact]
    public async Task Deleting_the_expert_takes_the_history_with_it()
    {
        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());

        (await staff.DeleteAsync($"/api/experts/{expert.Id}")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ProcessingRecords.AsNoTracking().AnyAsync(r => r.ExpertId == expert.Id))
            .Should().BeFalse();
    }

    /// <summary>
    /// The full round trip through the real host: a row is created, its basis moves the way an
    /// approved claim moves it, and the notice version acknowledged comes back exactly.
    /// </summary>
    [Fact]
    public async Task A_basis_transition_appends_and_the_acknowledged_version_survives_it()
    {
        var expert = await factory.CreateAuthenticatedClient()
            .CreateExpertAsync(ApiClientExtensions.NewExpert());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Built by hand rather than resolved: outside a request there is no principal, and the
        // host's scope provider fails closed to "owns nothing" — correctly, but it would 404 a
        // transition this test is not about ownership of.
        var records = new ProcessingRecordService(
            db, new UnrestrictedOwnershipScopeProvider(), TimeProvider.System);

        var before = await db.ProcessingRecords.AsNoTracking().SingleAsync(r => r.ExpertId == expert.Id);

        await records.AppendAsync(
            expert.Id, ProcessingOrigin.SelfRegistered, TransparencyNotice.CurrentVersion,
            "Claim on this row approved.");

        var history = await db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expert.Id).OrderBy(r => r.Sequence).ToListAsync();

        history.Should().HaveCount(2);
        history[0].Should().BeEquivalentTo(before, "the superseded record stays exactly as written");
        history[1].Basis.Should().Be(LawfulBasis.ContractNecessity);
        history[1].NoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);
        TransparencyNotice.Find(history[1].NoticeVersion).Should().NotBeNull();
    }
}
