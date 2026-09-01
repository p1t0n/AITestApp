using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Claims;
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
/// How a person comes to own a roster row (P1T-184), where the only evidence available is an email
/// address nobody ever verified and no mail can be sent to. Every rule here exists because that
/// evidence proves nothing: matching raises a request rather than binding anything, duplicates are
/// refused rather than guessed at, and the one mechanism that <em>is</em> proof — a code a Service
/// Manager handed over in person — is single-use.
///
/// <para>The database half (the partial unique indexes, the uniform 404s over HTTP) is proven
/// against real Postgres in <c>Web.Tests/ClaimBoundaryTests</c>.</para>
/// </summary>
public class ClaimServiceTests
{
    // ---- Registration matching ---------------------------------------------------------------

    [Fact]
    public async Task An_unmatched_address_gets_a_fresh_row_owned_immediately()
    {
        await using var world = await World.CreateAsync();
        var user = await world.RegisterAsync("nobody@example.com");

        var result = await world.Claims.BindOnRegistrationAsync(
            user.Id, "nobody@example.com", TransparencyNotice.CurrentVersion);

        result.Outcome.Should().Be(RegistrationBinding.OwnsNewRow);
        result.ExpertId.Should().NotBeNull();

        var row = await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == result.ExpertId);
        row.OwnerUserId.Should().Be(user.Id);
        row.Status.Should().Be(ExpertStatus.Active, "nobody staged this row — the person did");

        // Their own row, so nobody had to judge anything: no claim was raised at all.
        (await world.Claims.OpenAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The row a self-registering person creates is theirs because they asked to be considered for
    /// work — which is the pre-contractual measure Art. 6(1)(b) turns on. It is the one creation
    /// path in the system that is not <c>StaffCreated</c>, and it carries the notice version they
    /// acknowledged, because that acknowledgment is what makes the record provable later.
    /// </summary>
    [Fact]
    public async Task The_row_registration_creates_records_self_registration_and_the_notice_version()
    {
        await using var world = await World.CreateAsync();
        var user = await world.RegisterAsync("fresh@example.com");

        var result = await world.Claims.BindOnRegistrationAsync(
            user.Id, "fresh@example.com", TransparencyNotice.CurrentVersion);

        var records = await world.Db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == result.ExpertId).ToListAsync();

        records.Should().ContainSingle("the basis is written in the same transaction as the row");
        records[0].Origin.Should().Be(ProcessingOrigin.SelfRegistered);
        records[0].Basis.Should().Be(LawfulBasis.ContractNecessity);
        records[0].NoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);
        records[0].Sequence.Should().Be(1);
    }

    [Fact]
    public async Task Matching_ignores_case()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("Grace.Hopper@Example.com");
        var user = await world.RegisterAsync("grace.hopper@example.com");

        var result = await world.Claims.BindOnRegistrationAsync(
            user.Id, "GRACE.HOPPER@EXAMPLE.COM", TransparencyNotice.CurrentVersion);

        result.Outcome.Should().Be(RegistrationBinding.ClaimPending);
        result.ExpertId.Should().Be(expertId);
    }

    /// <summary>
    /// A pending claim grants nothing. Asserted on the column rather than through a service, because
    /// "owns nothing" is what makes the claim indistinguishable from no access at all (P1T-182) —
    /// every own-row endpoint 404s uniformly for both, and the HTTP half of that is asserted in
    /// <c>Web.Tests/ClaimBoundaryTests</c>.
    /// </summary>
    [Fact]
    public async Task A_pending_claim_binds_nothing_and_moves_no_basis()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("ada@example.com");
        var user = await world.RegisterAsync("ada@example.com");

        await world.Claims.BindOnRegistrationAsync(
            user.Id, "ada@example.com", TransparencyNotice.CurrentVersion);

        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .OwnerUserId.Should().BeNull();
        (await world.Records.CurrentAsync(expertId)).Basis.Should().Be(LawfulBasis.LegitimateInterest);
    }

    /// <summary>
    /// <c>Expert.Email</c> is unique only among Active rows, so two rows sharing an address is a real
    /// state rather than a theoretical one. Picking one of them would hand somebody another person's
    /// CV on a coin flip, so the answer is no claim, a flag, and a human.
    /// </summary>
    [Fact]
    public async Task Two_matching_rows_produce_no_claim_and_a_raised_flag()
    {
        await using var world = await World.CreateAsync();
        await world.BenchRowAsync("twin@example.com");
        await world.BenchRowAsync("twin@example.com");
        var user = await world.RegisterAsync("twin@example.com");

        var result = await world.Claims.BindOnRegistrationAsync(
            user.Id, "twin@example.com", TransparencyNotice.CurrentVersion);

        result.Outcome.Should().Be(RegistrationBinding.AmbiguousRaised);
        result.ExpertId.Should().BeNull("no row was picked, because no row could be");
        result.MatchCount.Should().Be(2);

        var queue = await world.Claims.OpenAsync();
        queue.Should().ContainSingle();
        queue[0].State.Should().Be(ClaimState.Ambiguous);
        queue[0].ExpertId.Should().BeNull();
        queue[0].MatchCount.Should().Be(2);

        (await world.Db.Experts.AsNoTracking().Where(e => e.Email == "twin@example.com").ToListAsync())
            .Should().OnlyContain(e => e.OwnerUserId == null);
    }

    /// <summary>
    /// A Draft is agent-staged from a resume and no human has vetted it. Claiming one would hand
    /// over a row nobody has looked at, so drafts are invisible to matching — and the person gets
    /// their own row instead, which the partial unique index permits because it binds Active rows.
    /// </summary>
    [Fact]
    public async Task A_draft_row_cannot_be_claimed()
    {
        await using var world = await World.CreateAsync();
        var draftId = await world.BenchRowAsync("staged@example.com", ExpertStatus.Draft);
        var user = await world.RegisterAsync("staged@example.com");

        var result = await world.Claims.BindOnRegistrationAsync(
            user.Id, "staged@example.com", TransparencyNotice.CurrentVersion);

        result.Outcome.Should().Be(RegistrationBinding.OwnsNewRow);
        result.ExpertId.Should().NotBe(draftId);
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == draftId))
            .OwnerUserId.Should().BeNull();
        (await world.Claims.OpenAsync()).Should().BeEmpty();
    }

    /// <summary>The boundary this whole design exists for: a second account cannot claim a row that
    /// already belongs to somebody, and does not quietly get a copy of it either.</summary>
    [Fact]
    public async Task A_second_account_cannot_claim_an_already_owned_row()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("taken@example.com");
        var owner = await world.RegisterAsync("owner@example.com");
        await world.BindAsync(expertId, owner.Id);

        var intruder = await world.RegisterAsync("taken@example.com");
        var result = await world.Claims.BindOnRegistrationAsync(
            intruder.Id, "taken@example.com", TransparencyNotice.CurrentVersion);

        result.Outcome.Should().Be(RegistrationBinding.AmbiguousRaised);
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .OwnerUserId.Should().Be(owner.Id, "the row did not change hands");

        var queue = await world.Claims.OpenAsync();
        queue.Should().ContainSingle().Which.ExpertId.Should().BeNull();
    }

    // ---- Approval and revocation ---------------------------------------------------------------

    [Fact]
    public async Task Approval_binds_the_row_and_appends_the_move_to_contract_necessity()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("claimant@example.com");
        var user = await world.RegisterAsync("claimant@example.com");
        await world.Claims.BindOnRegistrationAsync(
            user.Id, "claimant@example.com", TransparencyNotice.CurrentVersion);

        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);
        var claimId = (await world.Claims.OpenAsync()).Single().Id;
        var decided = await world.Claims.ApproveAsync(claimId, staff.Id);

        decided.State.Should().Be(ClaimState.Approved);
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .OwnerUserId.Should().Be(user.Id);

        var history = await world.Records.HistoryAsync(expertId);
        history.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity);
        history[1].NoticeVersion.Should().Be(
            TransparencyNotice.CurrentVersion, "the version the claimant acknowledged is the provable part");

        // Kept, not deleted: "rejected, then claimed again by somebody else" is a sequence only a
        // retained row can express, and it is the sequence an audit asks about.
        (await world.Db.PendingClaims.AsNoTracking().CountAsync()).Should().Be(1);
        (await world.Claims.OpenAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The round trip. Revocation appends rather than rewrites, so the history keeps showing the row
    /// <em>was</em> on 6(1)(b) — which matters because it was scannable in that window, and an
    /// UPDATE would erase that silently (EDPB GL 05/2020 §123).
    /// </summary>
    [Fact]
    public async Task Revocation_appends_a_return_to_legitimate_interest_and_rewrites_nothing()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("roundtrip@example.com");
        var user = await world.RegisterAsync("roundtrip@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);

        await world.Claims.BindOnRegistrationAsync(
            user.Id, "roundtrip@example.com", TransparencyNotice.CurrentVersion);
        await world.Claims.ApproveAsync((await world.Claims.OpenAsync()).Single().Id, staff.Id);

        var beforeRevocation = await world.Db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expertId).OrderBy(r => r.Sequence).ToListAsync();

        await world.Claims.RevokeAsync(expertId, staff.Id);

        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .OwnerUserId.Should().BeNull("revoked means unowned");

        var after = await world.Db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expertId).OrderBy(r => r.Sequence).ToListAsync();

        after.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity, LawfulBasis.LegitimateInterest);
        after.Take(2).Should().BeEquivalentTo(
            beforeRevocation, "neither earlier row may be touched — the history is the artefact");
    }

    [Fact]
    public async Task A_decided_claim_cannot_be_decided_again()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("twice@example.com");
        var user = await world.RegisterAsync("twice@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);

        await world.Claims.BindOnRegistrationAsync(
            user.Id, "twice@example.com", TransparencyNotice.CurrentVersion);
        var claimId = (await world.Claims.OpenAsync()).Single().Id;
        await world.Claims.ApproveAsync(claimId, staff.Id);

        var again = async () => await world.Claims.ApproveAsync(claimId, staff.Id);
        await again.Should().ThrowAsync<ConflictException>();

        (await world.Records.HistoryAsync(expertId)).Should().HaveCount(
            2, "a replayed approval must not append a second basis move");
    }

    [Fact]
    public async Task A_raised_flag_cannot_be_approved_because_there_is_no_row_to_bind()
    {
        await using var world = await World.CreateAsync();
        await world.BenchRowAsync("twin@example.com");
        await world.BenchRowAsync("twin@example.com");
        var user = await world.RegisterAsync("twin@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);

        await world.Claims.BindOnRegistrationAsync(
            user.Id, "twin@example.com", TransparencyNotice.CurrentVersion);
        var flagId = (await world.Claims.OpenAsync()).Single().Id;

        var act = async () => await world.Claims.ApproveAsync(flagId, staff.Id);
        await act.Should().ThrowAsync<ConflictException>();

        // Dismissing it is the way out, and the flag is kept afterwards.
        (await world.Claims.RejectAsync(flagId, staff.Id)).State.Should().Be(ClaimState.Rejected);
        (await world.Claims.OpenAsync()).Should().BeEmpty();
        (await world.Db.PendingClaims.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Revoking_a_row_nobody_owns_is_a_conflict_not_a_silent_no_op()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("unowned@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);

        var act = async () => await world.Claims.RevokeAsync(expertId, staff.Id);
        await act.Should().ThrowAsync<ConflictException>();

        (await world.Records.HistoryAsync(expertId)).Should().ContainSingle(
            "nothing changed, so nothing may be appended");
    }

    // ---- Claim codes ---------------------------------------------------------------------------

    /// <summary>
    /// The only proof this service can offer that is stronger than an unverified email match, which
    /// is why redemption binds ownership with no approval step at all.
    /// </summary>
    [Fact]
    public async Task A_claim_code_binds_ownership_with_no_approval_step()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("bench@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);
        var person = await world.RegisterAsync("person@example.com");

        var issued = await world.Claims.IssueCodeAsync(expertId, staff.Id);
        issued.Code.Should().NotBeNullOrWhiteSpace();

        var bound = await world.Claims.RedeemCodeAsync(issued.Code, person.Id);

        bound.Should().Be(expertId);
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .OwnerUserId.Should().Be(person.Id);

        var history = await world.Records.HistoryAsync(expertId);
        history.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity);
    }

    [Fact]
    public async Task A_redeemed_code_cannot_be_replayed()
    {
        await using var world = await World.CreateAsync();
        var first = await world.BenchRowAsync("first@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);
        var person = await world.RegisterAsync("person@example.com");
        var other = await world.RegisterAsync("other@example.com");

        var issued = await world.Claims.IssueCodeAsync(first, staff.Id);
        await world.Claims.RedeemCodeAsync(issued.Code, person.Id);

        var replay = async () => await world.Claims.RedeemCodeAsync(issued.Code, other.Id);
        await replay.Should().ThrowAsync<ConflictException>();

        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == first))
            .OwnerUserId.Should().Be(person.Id, "the row did not move on the replay");
    }

    [Fact]
    public async Task A_code_that_was_never_issued_is_refused_the_same_way_a_spent_one_is()
    {
        await using var world = await World.CreateAsync();
        var person = await world.RegisterAsync("person@example.com");

        var act = async () => await world.Claims.RedeemCodeAsync("ZZZZZZZZ-ZZZZZZZZ", person.Id);
        var thrown = await act.Should().ThrowAsync<ConflictException>();

        // Same words as a replay: a redemption endpoint must not tell an attacker which of their
        // guesses was once a real code.
        thrown.Which.Message.Should().Be("This claim code is not valid. Ask for a new one.");
    }

    [Fact]
    public async Task The_plaintext_code_is_not_stored()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("bench@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);

        var issued = await world.Claims.IssueCodeAsync(expertId, staff.Id);
        var stored = await world.Db.ClaimCodes.AsNoTracking().SingleAsync();

        stored.CodeHash.Should().NotBe(issued.Code);
        stored.CodeHash.Should().Be(ClaimCode.HashOf(issued.Code));
        ClaimCode.HashOf(issued.Code.ToLowerInvariant().Replace("-", string.Empty))
            .Should().Be(stored.CodeHash, "case and the grouping dashes are not part of the secret");
    }

    [Fact]
    public async Task A_code_cannot_be_issued_for_a_row_that_already_belongs_to_somebody()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("bench@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);
        var owner = await world.RegisterAsync("owner@example.com");
        await world.BindAsync(expertId, owner.Id);

        var act = async () => await world.Claims.IssueCodeAsync(expertId, staff.Id);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Redeeming_a_code_resolves_the_open_claim_it_supersedes()
    {
        await using var world = await World.CreateAsync();
        var matched = await world.BenchRowAsync("person@example.com");
        var real = await world.BenchRowAsync("their-real-row@example.com");
        var staff = await world.RegisterAsync("staff@example.com", UserRole.ServiceManager);
        var person = await world.RegisterAsync("person@example.com");

        await world.Claims.BindOnRegistrationAsync(
            person.Id, "person@example.com", TransparencyNotice.CurrentVersion);
        (await world.Claims.OpenAsync()).Should().ContainSingle().Which.ExpertId.Should().Be(matched);

        var issued = await world.Claims.IssueCodeAsync(real, staff.Id);
        await world.Claims.RedeemCodeAsync(issued.Code, person.Id);

        (await world.Claims.OpenAsync()).Should().BeEmpty("the code settled what the claim was asking");
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == matched))
            .OwnerUserId.Should().BeNull();
        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == real))
            .OwnerUserId.Should().Be(person.Id);
    }

    // ---- Email immutability ---------------------------------------------------------------------

    /// <summary>
    /// The takeover reached through the my-account door instead. An owner who can edit their own
    /// address can point it at a bench member's and re-trigger matching — so the field is
    /// Service-Manager-only, and the refusal is loud rather than a silent no-op.
    /// </summary>
    [Fact]
    public async Task An_expert_cannot_change_the_email_on_their_own_row()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("mine@example.com");
        world.ScopeTo(OwnershipScope.OwnedBy(expertId));

        var act = async () => await world.Experts.UpdateAsync(
            expertId, new SaveExpertDto("Ada", "Lovelace", "Engineer", "someone.else@example.com",
                null, null, null, null));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();

        (await world.Db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId))
            .Email.Should().Be("mine@example.com");
    }

    [Fact]
    public async Task An_expert_may_still_save_the_rest_of_their_own_row()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("mine@example.com");
        world.ScopeTo(OwnershipScope.OwnedBy(expertId));

        var saved = await world.Experts.UpdateAsync(
            expertId, new SaveExpertDto("Ada", "Lovelace", "Principal Engineer", "MINE@example.com",
                null, "Lisbon", null, null));

        saved.Title.Should().Be("Principal Engineer", "only the address is frozen");
        saved.Email.Should().Be("mine@example.com", "and matching the current address is not a change");
    }

    [Fact]
    public async Task A_service_manager_can_change_an_experts_email()
    {
        await using var world = await World.CreateAsync();
        var expertId = await world.BenchRowAsync("old@example.com");

        var saved = await world.Experts.UpdateAsync(
            expertId, new SaveExpertDto("Ada", "Lovelace", "Engineer", "new@example.com",
                null, null, null, null));

        saved.Email.Should().Be("new@example.com");
    }

    /// <summary>
    /// The Application layer over an isolated in-memory store. The scope is mutable because two of
    /// these tests are about what changes when the caller stops being staff.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly MutableScope _scope;

        private World(ServiceProvider provider, MutableScope scope)
        {
            _provider = provider;
            _scope = scope;
        }

        public static Task<World> CreateAsync()
        {
            var scope = new MutableScope();
            var services = new ServiceCollection();
            services.AddApplication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"claims-{Guid.NewGuid()}"));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddSingleton<IOwnershipScopeProvider>(scope);
            return Task.FromResult(new World(services.BuildServiceProvider(), scope));
        }

        public AppDbContext Db => _provider.GetRequiredService<AppDbContext>();
        public IClaimService Claims => _provider.GetRequiredService<IClaimService>();
        public IExpertService Experts => _provider.GetRequiredService<IExpertService>();
        public IProcessingRecordService Records => _provider.GetRequiredService<IProcessingRecordService>();

        public void ScopeTo(OwnershipScope scope) => _scope.Current = scope;

        /// <summary>A staff-created bench row, exactly as <c>ExpertService</c> writes one.</summary>
        public async Task<Guid> BenchRowAsync(string email, ExpertStatus status = ExpertStatus.Active)
        {
            var expert = new Expert
            {
                Id = Guid.NewGuid(),
                Status = status,
                FirstName = "Bench",
                LastName = "Member",
                Title = "Engineer",
                Email = email,
            };
            expert.ProcessingRecords.Add(ProcessingRecord.For(
                expert.Id, 1, ProcessingOrigin.StaffCreated, null,
                "Added to the bench by a Service Manager.", DateTimeOffset.UtcNow));

            Db.Experts.Add(expert);
            await Db.SaveChangesAsync();
            return expert.Id;
        }

        public async Task<User> RegisterAsync(string email, UserRole role = UserRole.Expert)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email.ToLowerInvariant(),
                ControlWordHash = "not-a-real-hash",
                Role = role,
                AcknowledgedNoticeVersion = TransparencyNotice.CurrentVersion,
                NoticeAcknowledgedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        /// <summary>Points a row at an account without going through the claim flow.</summary>
        public async Task BindAsync(Guid expertId, Guid userId)
        {
            (await Db.Experts.SingleAsync(e => e.Id == expertId)).OwnerUserId = userId;
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

        private sealed class MutableScope : IOwnershipScopeProvider
        {
            public OwnershipScope Current { get; set; } = OwnershipScope.Unrestricted;

            public ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default) => new(Current);
        }
    }
}
