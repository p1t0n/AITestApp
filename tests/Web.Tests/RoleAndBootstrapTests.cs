using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Web.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// Where a role comes from. Three answers, and they have to be the right way round: a row written
/// before the split is staff (the migration's default), a self-serve signup is not, and the first
/// staff account on a fresh database comes from configuration — because nothing else could make one.
///
/// <para>Against the real Postgres the host migrated, so the migration's own default is what
/// answers here, not an EF model convention.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RoleAndBootstrapTests(WebApiFactory factory)
{
    /// <summary>
    /// The migration. A row inserted without naming a role is a row from before the split, and
    /// every account from then was staff — demoting them would lock everyone out of the app they
    /// administer. Raw SQL on purpose: EF would supply the model's value and prove nothing.
    /// </summary>
    [Fact]
    public async Task A_row_written_without_a_role_lands_as_service_manager()
    {
        var email = ApiClientExtensions.UniqueEmail("legacy");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "ControlWordHash", "Status", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {0}, '', 'Active', now(), now());
            """,
            email);

        var legacy = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        legacy.Role.Should().Be(UserRole.ServiceManager);
        legacy.TokenVersion.Should().Be(1, "a session generation of 0 could not be told from a missing claim");
    }

    /// <summary>
    /// Signup. The account the ceremony creates sets no role, so the domain default decides — and
    /// the default has to be Expert, or open self-serve signup would mint staff. (The ceremony
    /// itself needs a browser; the e2e suite drives it and asserts where an Expert lands.)
    /// </summary>
    [Fact]
    public void A_new_account_defaults_to_expert()
    {
        new User().Role.Should().Be(UserRole.Expert);
        new User().TokenVersion.Should().Be(1);
    }

    [Fact]
    public async Task The_bootstrap_invites_the_configured_email_when_it_has_no_account()
    {
        var email = ApiClientExtensions.UniqueEmail("first-staff");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outcome = await ServiceManagerBootstrapper.EnsureAsync(db, email, TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.Invited);
        var invited = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        invited.Role.Should().Be(UserRole.ServiceManager);
        invited.ControlWordHash.Should().BeEmpty("the invite is not a login until a passkey is enrolled");
    }

    [Fact]
    public async Task The_bootstrap_promotes_an_existing_expert_and_revokes_its_sessions()
    {
        var expert = factory.CreateAccount(UserRole.Expert);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outcome = await ServiceManagerBootstrapper.EnsureAsync(db, expert.Email, TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.Promoted);
        var promoted = await db.Users.AsNoTracking().SingleAsync(u => u.Id == expert.Id);
        promoted.Role.Should().Be(UserRole.ServiceManager);
        promoted.TokenVersion.Should().BeGreaterThan(
            expert.TokenVersion,
            "the old token still claims Expert, so it has to stop working");
    }

    /// <summary>
    /// Idempotent, because it runs on every startup. A second pass must not create a second row,
    /// and must not bump the token version of an account that was already staff — that would sign
    /// everyone out on every deploy.
    /// </summary>
    [Fact]
    public async Task The_bootstrap_is_idempotent()
    {
        var email = ApiClientExtensions.UniqueEmail("repeat-staff");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await ServiceManagerBootstrapper.EnsureAsync(db, email, TimeProvider.System);
        var afterFirst = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);

        var second = await ServiceManagerBootstrapper.EnsureAsync(db, email, TimeProvider.System);

        second.Should().Be(BootstrapOutcome.AlreadyServiceManager);
        var rows = await db.Users.AsNoTracking().Where(u => u.Email == email).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].TokenVersion.Should().Be(afterFirst.TokenVersion);
    }

    /// <summary>
    /// Adoption, at the level where it went wrong: the invite row is loaded, given the control word
    /// the ceremony collected, and handed a passkey — and it keeps its id and its role, because the
    /// operator's account has to be the account the bootstrap promised.
    ///
    /// <para>The passkey is added through its own set with the FK set by hand. Added through
    /// <c>user.Passkeys</c> it is tracked as Modified — a Guid key is store-generated by convention,
    /// so an entity found on a navigation with its key already filled in looks like an existing row
    /// — and the save fails with a concurrency error on a row that was never there. That is exactly
    /// what the signup ceremony hit, and it only shows up against a real database.</para>
    /// </summary>
    [Fact]
    public async Task An_invite_row_can_be_adopted_by_enrolling_a_passkey()
    {
        var email = ApiClientExtensions.UniqueEmail("adopt");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ServiceManagerBootstrapper.EnsureAsync(db, email, TimeProvider.System);
        var invite = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);

        // What AuthController.SignupComplete does when it finds an invite for the address.
        var adopting = await db.Users.Include(u => u.Passkeys)
            .SingleAsync(u => u.Email == email && u.ControlWordHash == string.Empty && !u.Passkeys.Any());
        adopting.ControlWordHash = "hashed-control-word";
        db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = adopting.Id,
            CredentialId = Guid.NewGuid().ToByteArray(),
            PublicKey = [1, 2, 3],
            SignatureCounter = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var adopted = await db.Users.AsNoTracking().Include(u => u.Passkeys)
            .SingleAsync(u => u.Email == email);
        adopted.Id.Should().Be(invite.Id, "the passkey was registered against the invite's user handle");
        adopted.Role.Should().Be(UserRole.ServiceManager);
        adopted.Passkeys.Should().HaveCount(1);

        // And it is no longer adoptable: a real account must never be taken over by a second signup.
        var stillAdoptable = await db.Users.Include(u => u.Passkeys)
            .AnyAsync(u => u.Email == email && u.ControlWordHash == string.Empty && !u.Passkeys.Any());
        stillAdoptable.Should().BeFalse();
    }

    [Fact]
    public async Task An_unconfigured_bootstrap_does_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outcome = await ServiceManagerBootstrapper.EnsureAsync(db, "   ", TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.NotConfigured);
    }
}
