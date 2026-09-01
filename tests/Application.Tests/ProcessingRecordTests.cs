using System.Reflection;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The compliance spine (P1T-183): why we are allowed to hold each roster row, recorded per row,
/// append-only, with the transparency-notice version the person acknowledged recoverable afterwards.
///
/// <para>The database half of this — the append-only trigger and the origin/basis CHECK constraint —
/// is proven against real Postgres in <c>Web.Tests/ProcessingRecordDatabaseTests</c>. EF InMemory
/// enforces neither, so asserting them here would assert nothing.</para>
/// </summary>
public class ProcessingRecordTests
{
    // ---- Basis per origin -------------------------------------------------------------------

    [Theory]
    [InlineData(ProcessingOrigin.SelfRegistered, LawfulBasis.ContractNecessity)]
    [InlineData(ProcessingOrigin.StaffCreated, LawfulBasis.LegitimateInterest)]
    public void Origin_decides_the_basis(ProcessingOrigin origin, LawfulBasis expected)
    {
        ProcessingRecord.BasisFor(origin).Should().Be(expected);

        ProcessingRecord.For(Guid.NewGuid(), 1, origin, null, "because", DateTimeOffset.UtcNow)
            .Basis.Should().Be(expected, "the factory is the only place a basis is chosen");
    }

    /// <summary>
    /// Structural, not a list: adding an origin without deciding its lawful basis fails here rather
    /// than defaulting to a lawful-looking one somewhere downstream. That default is the "global
    /// default path" this ticket exists to prevent.
    /// </summary>
    [Fact]
    public void Every_origin_has_a_decided_basis()
    {
        var origins = Enum.GetValues<ProcessingOrigin>();
        origins.Should().HaveCountGreaterThanOrEqualTo(2);

        foreach (var origin in origins)
        {
            var basis = () => ProcessingRecord.BasisFor(origin);
            basis.Should().NotThrow($"{origin} has no lawful basis decided for it");
            Enum.IsDefined(ProcessingRecord.BasisFor(origin)).Should().BeTrue();
        }
    }

    [Fact]
    public void An_undefined_origin_has_no_basis_at_all()
    {
        var act = () => ProcessingRecord.BasisFor((ProcessingOrigin)99);
        act.Should().Throw<ArgumentOutOfRangeException>(
            "falling back to some basis for an origin nobody decided is exactly the failure mode");
    }

    // ---- Every creation path records one -----------------------------------------------------

    /// <summary>
    /// The audit. Reflected over <see cref="IExpertService"/> rather than over a list of methods
    /// somebody remembered, because a new way to create an Expert that forgets its lawful basis is
    /// silent: the row appears, the roster works, and the defect only surfaces in an audit.
    /// </summary>
    [Fact]
    public async Task Every_way_of_creating_an_expert_records_a_lawful_basis()
    {
        var creators = typeof(IExpertService).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(SaveExpertDto)))
            .Where(m => m.GetParameters().All(p => p.ParameterType != typeof(Guid)))
            .ToList();

        creators.Should().HaveCountGreaterThanOrEqualTo(
            2, "the API's create and the ingestion agent's draft are both creation paths");

        foreach (var method in creators)
        {
            await using var world = await World.CreateAsync();
            var created = await world.CreateThroughAsync(method);

            var records = await world.Db.ProcessingRecords
                .AsNoTracking().Where(r => r.ExpertId == created).ToListAsync();

            records.Should().ContainSingle(
                $"{method.Name} must write the row's lawful basis in the same transaction as the row");
            records[0].Origin.Should().Be(ProcessingOrigin.StaffCreated);
            records[0].Basis.Should().Be(LawfulBasis.LegitimateInterest);
            records[0].Sequence.Should().Be(1);
            records[0].NoticeVersion.Should().BeNull(
                "nobody was shown a notice — this is the Art. 14 population, and we send no email");
            records[0].Reason.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// The compliance defect stated as a check over the whole store: an Expert with no recorded
    /// basis. Run after exercising every creation path, so it is not vacuous.
    /// </summary>
    [Fact]
    public async Task No_expert_exists_without_a_recorded_basis()
    {
        await using var world = await World.CreateAsync();
        await world.Experts.CreateAsync(NewExpert());
        await world.Experts.CreateDraftAsync(NewExpert());

        var orphans = await world.Db.Experts
            .AsNoTracking()
            .Where(e => !world.Db.ProcessingRecords.Any(r => r.ExpertId == e.Id))
            .Select(e => e.Email)
            .ToListAsync();

        orphans.Should().BeEmpty(
            "an Expert with no recorded lawful basis is a compliance defect, not a gap to fill later");
    }

    // ---- Append-only ------------------------------------------------------------------------

    [Fact]
    public async Task A_transition_appends_and_leaves_the_previous_record_untouched()
    {
        await using var world = await World.CreateAsync();
        var expertId = (await world.Experts.CreateAsync(NewExpert())).Id;

        var before = await world.Db.ProcessingRecords
            .AsNoTracking().SingleAsync(r => r.ExpertId == expertId);

        // The claim flow's move: legitimate interest → pre-contractual necessity (P1T-184).
        var appended = await world.Records.AppendAsync(
            expertId, ProcessingOrigin.SelfRegistered, TransparencyNotice.CurrentVersion,
            "Claim on this row approved; the person registered and acknowledged the notice.");

        appended.Sequence.Should().Be(2);
        appended.Basis.Should().Be(LawfulBasis.ContractNecessity);

        var after = await world.Db.ProcessingRecords
            .AsNoTracking().SingleAsync(r => r.Id == before.Id);

        // Field by field rather than a reference comparison: "the old row still says exactly what
        // it said" is the claim, and EDPB GL 05/2020 §123 is why it matters.
        after.Should().BeEquivalentTo(before);

        var history = await world.Records.HistoryAsync(expertId);
        history.Select(r => r.Sequence).Should().Equal(1, 2);
        history.Select(r => r.Basis).Should()
            .Equal(LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity);

        (await world.Records.CurrentAsync(expertId)).Sequence.Should().Be(2);
    }

    /// <summary>
    /// Revocation (P1T-173 §8). The row returns to legitimate interest by appending, so the history
    /// still shows it <em>was</em> on 6(1)(b) for a window — which is a fact with consequences,
    /// because it was scannable then.
    /// </summary>
    [Fact]
    public async Task Revocation_appends_a_third_row_rather_than_deleting_the_second()
    {
        await using var world = await World.CreateAsync();
        var expertId = (await world.Experts.CreateAsync(NewExpert())).Id;

        await world.Records.AppendAsync(
            expertId, ProcessingOrigin.SelfRegistered, TransparencyNotice.CurrentVersion, "Claim approved.");
        await world.Records.AppendAsync(
            expertId, ProcessingOrigin.StaffCreated, null, "Ownership revoked by a Service Manager.");

        var history = await world.Records.HistoryAsync(expertId);
        history.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity, LawfulBasis.LegitimateInterest);
    }

    // ---- Round-trip provability ---------------------------------------------------------------

    [Fact]
    public async Task The_exact_notice_version_acknowledged_is_recoverable_afterwards()
    {
        await using var world = await World.CreateAsync();
        var expertId = (await world.Experts.CreateAsync(NewExpert())).Id;

        var acknowledged = TransparencyNotice.CurrentVersion;
        await world.Records.AppendAsync(
            expertId, ProcessingOrigin.SelfRegistered, acknowledged, "Claim approved.");

        var current = await world.Records.CurrentAsync(expertId);
        current.NoticeVersion.Should().Be(acknowledged);

        // The version string alone proves nothing — the words behind it have to come back too.
        var notice = TransparencyNotice.Find(current.NoticeVersion);
        notice.Should().NotBeNull();
        notice!.Version.Should().Be(acknowledged);
        notice.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_version_that_was_never_published_cannot_be_recorded()
    {
        await using var world = await World.CreateAsync();
        var expertId = (await world.Experts.CreateAsync(NewExpert())).Id;

        var act = async () => await world.Records.AppendAsync(
            expertId, ProcessingOrigin.SelfRegistered, "1999-01-01", "Claim approved.");

        await act.Should().ThrowAsync<ArgumentException>(
            "an acknowledgment whose text cannot be recovered is not provable, so it is not recordable");
    }

    [Fact]
    public async Task Acknowledging_a_new_notice_version_does_not_move_the_basis()
    {
        await using var world = await World.CreateAsync();
        var expertId = (await world.Experts.CreateAsync(NewExpert())).Id;

        var appended = await world.Records.AcknowledgeNoticeAsync(
            expertId, TransparencyNotice.CurrentVersion);

        appended.Sequence.Should().Be(2);
        appended.NoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);
        appended.Basis.Should().Be(
            LawfulBasis.LegitimateInterest,
            "reading an updated notice is not a change in the relationship, so it cannot change the ground");
        appended.Origin.Should().Be(ProcessingOrigin.StaffCreated);
    }

    [Fact]
    public async Task A_row_with_no_record_at_all_is_a_defect_the_reads_refuse()
    {
        await using var world = await World.CreateAsync();
        var orphan = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "No",
            LastName = "Basis",
            Title = "Engineer",
            Email = $"orphan-{Guid.NewGuid():N}@example.com",
        };
        world.Db.Experts.Add(orphan);
        await world.Db.SaveChangesAsync();

        var act = async () => await world.Records.CurrentAsync(orphan.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- The notice itself --------------------------------------------------------------------

    [Fact]
    public void The_current_notice_is_published_and_recoverable_by_version()
    {
        TransparencyNotice.IsPublished(TransparencyNotice.CurrentVersion).Should().BeTrue();
        TransparencyNotice.Find(TransparencyNotice.CurrentVersion)!.Text
            .Should().NotBeNullOrWhiteSpace();
        TransparencyNotice.IsPublished("nope").Should().BeFalse();
        TransparencyNotice.IsPublished(null).Should().BeFalse();
        TransparencyNotice.All.Should().Contain(n => n.Version == TransparencyNotice.CurrentVersion);
    }

    /// <summary>
    /// Art. 5(1)(a): a notice that creates a false impression is itself a transparency breach. The
    /// four things this notice must say out loud are the four a bench is tempted to leave out.
    /// </summary>
    [Theory]
    [InlineData("score", "that software scores and ranks people is the point of disclosing anything")]
    [InlineData("rank", "ranking against other people is a distinct fact from scoring")]
    [InlineData("Service Managers", "staff keep full write; no wording may imply exclusive authorship")]
    [InlineData("erase", "erasure is one of the three rights the resolution names")]
    [InlineData("6(1)(b)", "the basis per origin is stated, not implied")]
    [InlineData("6(1)(f)", "the basis per origin is stated, not implied")]
    [InlineData("never sends email", "the Art. 13-on-change gap is disclosed rather than hidden")]
    public void The_notice_says_the_things_it_is_tempting_to_leave_out(string phrase, string why)
    {
        TransparencyNotice.Current.Text.Should().Contain(phrase, why);
    }

    [Fact]
    public void The_notice_never_claims_the_person_controls_their_data()
    {
        // Service Managers keep full write and staff-created rows exist the Expert never authored,
        // so this sentence would be false — and a false notice is the breach, not the shortcut.
        TransparencyNotice.Current.Text.Should().NotContain("you control your data");
        TransparencyNotice.Current.Text.Should().NotContain("your data is yours");
    }

    /// <summary>The Art. 9 stance, as text a person actually reads rather than as a manual entry.</summary>
    [Fact]
    public void The_notice_asks_people_to_leave_special_category_detail_out()
    {
        var text = TransparencyNotice.Current.Text;
        text.Should().Contain("health");
        text.Should().Contain("trade-union");
        text.Should().Contain("infer");
    }

    // ---- Notify, don't gate -------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Expert, null, true)]
    [InlineData(UserRole.Expert, "1999-01-01", true)]
    [InlineData(UserRole.ServiceManager, null, false)]
    public void A_newer_notice_is_surfaced_to_the_people_it_is_addressed_to(
        UserRole role, string? acknowledged, bool expectPending)
    {
        var pending = TransparencyNotice.PendingFor(role, acknowledged);
        (pending is not null).Should().Be(expectPending);
        if (expectPending)
        {
            pending.Should().Be(TransparencyNotice.CurrentVersion);
        }
    }

    [Fact]
    public void An_up_to_date_expert_is_told_nothing()
    {
        TransparencyNotice.PendingFor(UserRole.Expert, TransparencyNotice.CurrentVersion)
            .Should().BeNull();
    }

    private static SaveExpertDto NewExpert() => new(
        "Ada", "Lovelace", "Engineer", $"ada-{Guid.NewGuid():N}@example.com", null, null, null, null);

    /// <summary>The Application layer over an isolated in-memory store, unrestricted — this fixture
    /// is about lawful basis, not about who is asking (that is the ownership audit's job).</summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private World(ServiceProvider provider) => _provider = provider;

        public static Task<World> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddApplication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"basis-{Guid.NewGuid()}"));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddSingleton<IOwnershipScopeProvider, UnrestrictedOwnershipScopeProvider>();
            return Task.FromResult(new World(services.BuildServiceProvider()));
        }

        public AppDbContext Db => _provider.GetRequiredService<AppDbContext>();
        public IExpertService Experts => _provider.GetRequiredService<IExpertService>();
        public IProcessingRecordService Records => _provider.GetRequiredService<IProcessingRecordService>();

        /// <summary>Calls one creation method reflectively and digs the new row's id out of
        /// whatever shape it returns.</summary>
        public async Task<Guid> CreateThroughAsync(MethodInfo method)
        {
            var args = method.GetParameters()
                .Select(object? (p) => p.ParameterType == typeof(CancellationToken)
                    ? CancellationToken.None
                    : NewExpert())
                .ToArray();

            var task = (Task)method.Invoke(Experts, args)!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;

            return result switch
            {
                ExpertDetailDto detail => detail.Id,
                IngestionDraftDto draft => draft.Expert.Id,
                _ => throw new InvalidOperationException(
                    $"{method.Name} returns a {result.GetType().Name} this audit cannot read an id " +
                    "out of. Teach it rather than excluding the method — an unaudited creation path " +
                    "is exactly the hole being hunted."),
            };
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
