using System.Net;
using System.Net.Http.Json;
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
/// The claim surface at the HTTP boundary and against real Postgres (P1T-184). Three things can
/// only be proven here: that a pending claim is <em>indistinguishable</em> from no access at all,
/// that the partial unique indexes actually refuse a second open claim, and that the queue and the
/// codes are staff-only in the running host rather than in a policy somebody meant to add.
/// </summary>
[Collection(WebApiCollection.Name)]
public class ClaimBoundaryTests(WebApiFactory factory)
{
    // ---- Who may reach this surface ------------------------------------------------------------

    [Fact]
    public async Task The_queue_and_the_codes_are_staff_only()
    {
        using var expert = factory.CreateExpertClient();

        (await expert.GetAsync("/api/claims")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await expert.GetAsync($"/api/claims/ownership/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await expert.PostAsJsonAsync("/api/claims/codes", new { expertId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await expert.PostAsJsonAsync("/api/claims/revoke", new { expertId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await expert.PostAsJsonAsync($"/api/claims/{Guid.NewGuid()}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Redemption is the one action here an Expert may take — it is how they stop owning
    /// nothing, so it cannot require already owning something.</summary>
    [Fact]
    public async Task An_expert_may_reach_redemption_and_nothing_else_here()
    {
        using var expert = factory.CreateExpertClient();

        var response = await expert.PostAsJsonAsync(
            "/api/claims/redeem", new { code = "ZZZZZZZZ-ZZZZZZZZ-ZZZZZZZZ-ZZZZZZZZ" });

        // Refused on the merits of the code, not by the policy: 409, not 403.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- A pending claim grants nothing ---------------------------------------------------------

    /// <summary>
    /// The one that matters (P1T-173): while a claim waits, the claimant's session must look exactly
    /// like a session that never claimed anything. Not a bespoke guard — it falls out of owning no
    /// row (P1T-182) — so what this asserts is that the claim flow did not accidentally grant
    /// something on the side.
    /// </summary>
    [Fact]
    public async Task A_pending_claim_is_indistinguishable_from_no_access_at_all()
    {
        var staff = factory.CreateAuthenticatedClient();
        var email = ApiClientExtensions.UniqueEmail("claimed");
        var target = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert(email: email));

        var claimant = factory.CreateAccount(UserRole.Expert);
        SetAccountEmail(claimant.Id, email);
        var binding = await BindOnRegistrationAsync(claimant.Id, email);
        binding.Outcome.Should().Be(RegistrationBinding.ClaimPending);

        using var claimantClient = factory.ClientForAccount(claimant);
        using var stranger = factory.CreateExpertClient();

        foreach (var path in new[]
                 {
                     $"/api/experts/{target.Id}",
                     $"/api/experts/{target.Id}/availability",
                 })
        {
            var withClaim = await claimantClient.GetAsync(path);
            var withoutClaim = await stranger.GetAsync(path);

            withClaim.StatusCode.Should().Be(HttpStatusCode.NotFound);
            withClaim.StatusCode.Should().Be(withoutClaim.StatusCode,
                $"a pending claim must not change what {path} answers");
        }

        factory.OwnerOf(target.Id).Should().BeNull();
    }

    // ---- Database truth --------------------------------------------------------------------------

    /// <summary>
    /// The partial unique index, not a service convention: one open claim per account. EF InMemory
    /// has no indexes at all, so this can only be shown against the real engine.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_second_open_claim_from_one_account()
    {
        var staff = factory.CreateAuthenticatedClient();
        var first = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var second = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var claimant = factory.CreateAccount(UserRole.Expert);

        AddClaimDirectly(claimant.Id, first.Id);
        var again = () => AddClaimDirectly(claimant.Id, second.Id);

        again.Should().Throw<DbUpdateException>(
            "one person may have one claim open at a time, and the database is what says so");
    }

    [Fact]
    public async Task The_database_refuses_two_open_claims_on_one_row()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());

        AddClaimDirectly(factory.CreateAccount(UserRole.Expert).Id, row.Id);
        var second = () => AddClaimDirectly(factory.CreateAccount(UserRole.Expert).Id, row.Id);

        second.Should().Throw<DbUpdateException>();
    }

    /// <summary>
    /// The reachable half of "never auto-pick": the address matches exactly one row and that row
    /// already belongs to somebody. No claim is created, a flag is raised, and the row does not
    /// change hands — this is the takeover attempt the whole design exists to refuse.
    ///
    /// <para>Its sibling — two Active rows sharing an address — is asserted in
    /// <c>Application.Tests/ClaimServiceTests</c> rather than here, because this database cannot
    /// currently hold that state: <c>IX_Experts_Email</c> is unique across Active rows and drafts
    /// are excluded from matching, so the duplicate is unreachable through Postgres. The rule is
    /// kept anyway, because what makes it unreachable is one index filter (P1T-185 adds a state
    /// that deliberately keeps rows Active), and a matching rule that quietly starts guessing the
    /// day that filter changes is not a rule.</para>
    /// </summary>
    [Fact]
    public async Task A_match_on_a_row_somebody_already_owns_raises_a_flag_and_binds_nothing()
    {
        var staff = factory.CreateAuthenticatedClient();
        var email = ApiClientExtensions.UniqueEmail("taken");
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert(email: email));
        var owner = factory.CreateAccount(UserRole.Expert);
        factory.SetOwner(row.Id, owner.Id);

        var intruder = factory.CreateAccount(UserRole.Expert);
        SetAccountEmail(intruder.Id, email);

        var binding = await BindOnRegistrationAsync(intruder.Id, email);

        binding.Outcome.Should().Be(RegistrationBinding.AmbiguousRaised);
        binding.ExpertId.Should().BeNull();
        factory.OwnerOf(row.Id).Should().Be(owner.Id, "the row did not change hands");

        var queue = await (await staff.GetAsync("/api/claims")).ReadOkAsync<List<ClaimQueueItemDto>>();
        queue.Should().Contain(c => c.ClaimantUserId == intruder.Id
                                    && c.State == ClaimState.Ambiguous
                                    && c.ExpertId == null);
    }

    // ---- The round trip over HTTP -----------------------------------------------------------------

    [Fact]
    public async Task Approve_then_revoke_appends_twice_and_rewrites_nothing()
    {
        var staff = factory.CreateAuthenticatedClient();
        var email = ApiClientExtensions.UniqueEmail("roundtrip");
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert(email: email));

        var claimant = factory.CreateAccount(UserRole.Expert);
        SetAccountEmail(claimant.Id, email, TransparencyNotice.CurrentVersion);
        await BindOnRegistrationAsync(claimant.Id, email);

        var queue = await (await staff.GetAsync("/api/claims")).ReadOkAsync<List<ClaimQueueItemDto>>();
        var claim = queue.Single(c => c.ClaimantUserId == claimant.Id);
        claim.ExpertId.Should().Be(row.Id);

        (await staff.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        factory.OwnerOf(row.Id).Should().Be(claimant.Id);

        var afterApproval = HistoryOf(row.Id);
        afterApproval.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity);
        afterApproval[1].NoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);

        (await staff.PostAsJsonAsync("/api/claims/revoke", new { expertId = row.Id }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.OwnerOf(row.Id).Should().BeNull();

        var afterRevocation = HistoryOf(row.Id);
        afterRevocation.Select(r => r.Basis).Should().Equal(
            LawfulBasis.LegitimateInterest, LawfulBasis.ContractNecessity, LawfulBasis.LegitimateInterest);
        afterRevocation.Take(2).Should().BeEquivalentTo(
            afterApproval, "the append-only trigger and the design agree: earlier rows are never touched");
    }

    [Fact]
    public async Task A_claim_code_is_single_use_over_http()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());

        var issued = await (await staff.PostAsJsonAsync("/api/claims/codes", new { expertId = row.Id }))
            .ReadOkAsync<ClaimCodeIssuedDto>();

        var first = factory.CreateAccount(UserRole.Expert);
        var second = factory.CreateAccount(UserRole.Expert);
        using var firstClient = factory.ClientForAccount(first);
        using var secondClient = factory.ClientForAccount(second);

        var redeemed = await (await firstClient.PostAsJsonAsync(
            "/api/claims/redeem", new { code = issued.Code })).ReadOkAsync<RedeemedRow>();
        redeemed.ExpertId.Should().Be(row.Id);
        factory.OwnerOf(row.Id).Should().Be(first.Id);

        var replay = await secondClient.PostAsJsonAsync("/api/claims/redeem", new { code = issued.Code });
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        factory.OwnerOf(row.Id).Should().Be(first.Id, "the replay moved nothing");
    }

    // ---- Email immutability at the boundary ---------------------------------------------------------

    [Fact]
    public async Task An_expert_cannot_change_their_own_email_but_a_service_manager_can()
    {
        var staff = factory.CreateAuthenticatedClient();
        var row = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (owner, _) = factory.CreateExpertClientOwning(row.Id);
        using var _owner = owner;

        var theirAttempt = await owner.PutAsJsonAsync(
            $"/api/experts/{row.Id}",
            Save(row.FirstName, row.LastName, row.Title, ApiClientExtensions.UniqueEmail("elsewhere")),
            WebApiFactory.Json);

        theirAttempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        EmailOf(row.Id).Should().Be(row.Email);

        var staffEmail = ApiClientExtensions.UniqueEmail("moved");
        var staffAttempt = await staff.PutAsJsonAsync(
            $"/api/experts/{row.Id}", Save(row.FirstName, row.LastName, row.Title, staffEmail),
            WebApiFactory.Json);

        staffAttempt.StatusCode.Should().Be(HttpStatusCode.OK);
        EmailOf(row.Id).Should().Be(staffEmail);
    }

    // ---- Helpers -------------------------------------------------------------------------------------

    private sealed record RedeemedRow(Guid ExpertId);

    private static object Save(string first, string last, string title, string email) => new
    {
        firstName = first,
        lastName = last,
        title,
        email,
        phone = (string?)null,
        location = (string?)null,
        summary = (string?)null,
        photoUrl = (string?)null,
    };

    private async Task<RegistrationBindingDto> BindOnRegistrationAsync(Guid userId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var claims = scope.ServiceProvider.GetRequiredService<IClaimService>();
        return await claims.BindOnRegistrationAsync(userId, email, TransparencyNotice.CurrentVersion);
    }

    private void SetAccountEmail(Guid userId, string email, string? noticeVersion = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Id == userId);
        user.Email = email.ToLowerInvariant();
        user.AcknowledgedNoticeVersion = noticeVersion ?? user.AcknowledgedNoticeVersion;
        db.SaveChanges();
    }

    private void AddClaimDirectly(Guid claimantUserId, Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PendingClaims.Add(new PendingClaim
        {
            Id = Guid.NewGuid(),
            ClaimantUserId = claimantUserId,
            ClaimantEmail = "direct@example.com",
            ExpertId = expertId,
            MatchCount = 1,
            State = ClaimState.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private List<ProcessingRecord> HistoryOf(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expertId).OrderBy(r => r.Sequence).ToList();
    }

    private string EmailOf(Guid expertId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Experts.AsNoTracking().Single(e => e.Id == expertId).Email;
    }
}
