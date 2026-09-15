using ExpertToJob.Application.Users;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Web.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// Where a role comes from. Three answers, and they have to be the right way round: a row that
/// names no role is refused outright, a self-serve signup is a User, and the first Administrator on
/// a fresh database comes from configuration — because nothing else could make one.
///
/// <para>Against the real Postgres the host migrated, so the schema is what answers here, not an EF
/// model convention.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class RoleAndBootstrapTests(WebApiFactory factory)
{
    /// <summary>
    /// The schema, after the rename dropped the column default (P1T-236). Until then a row written
    /// without a role became a <c>ServiceManager</c>, which spoke for accounts predating the split;
    /// keeping that past the rename would mean an omission mints an Administrator. Now the write is
    /// refused. Raw SQL on purpose: EF would supply the model's value and prove nothing.
    /// </summary>
    [Fact]
    public async Task A_row_written_without_a_role_is_refused()
    {
        var email = ApiClientExtensions.UniqueEmail("roleless");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var write = () => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "ControlWordHash", "Status", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {0}, '', 'Active', now(), now());
            """,
            email);

        await write.Should().ThrowAsync<Exception>();
        (await db.Users.AsNoTracking().AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    /// <summary>
    /// The other half of the same guard: a role the enum does not have never reaches a column EF
    /// will later try to materialise. Raw SQL is the only way in — the enum closes every other door.
    /// </summary>
    [Fact]
    public async Task A_role_the_enum_does_not_have_is_refused()
    {
        var email = ApiClientExtensions.UniqueEmail("ghost");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var write = () => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "ControlWordHash", "Status", "Role", "TokenVersion",
                                 "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), {0}, '', 'Active', 'ServiceManager', 1, now(), now());
            """,
            email);

        (await write.Should().ThrowAsync<Exception>())
            .Which.GetBaseException().Message.Should().Contain("CK_Users_Role");
    }

    /// <summary>
    /// Signup. The account the ceremony creates sets no role, so the domain default decides — and
    /// the default has to be User, or open self-serve signup would mint staff. (The ceremony itself
    /// needs a browser; the e2e suite drives it and asserts where a User lands.)
    /// </summary>
    [Fact]
    public void A_new_account_defaults_to_the_user_role()
    {
        new User().Role.Should().Be(UserRole.User);
        new User().TokenVersion.Should().Be(1);
    }

    [Fact]
    public async Task The_bootstrap_invites_the_configured_email_when_it_has_no_account()
    {
        var email = ApiClientExtensions.UniqueEmail("first-staff");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        var outcome = await AdministratorBootstrapper.EnsureAsync(db, users, email, TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.Invited);
        var invited = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        invited.Role.Should().Be(UserRole.Administrator);
        invited.ControlWordHash.Should().BeEmpty("the invite is not a login until a passkey is enrolled");
    }

    [Fact]
    public async Task The_bootstrap_promotes_an_existing_user_and_revokes_its_sessions()
    {
        var expert = factory.CreateAccount(UserRole.User);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        var outcome = await AdministratorBootstrapper.EnsureAsync(db, users, expert.Email, TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.Promoted);
        var promoted = await db.Users.AsNoTracking().SingleAsync(u => u.Id == expert.Id);
        promoted.Role.Should().Be(UserRole.Administrator);
        promoted.TokenVersion.Should().BeGreaterThan(
            expert.TokenVersion,
            "the old token still claims User, so it has to stop working");
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
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        await AdministratorBootstrapper.EnsureAsync(db, users, email, TimeProvider.System);
        var afterFirst = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);

        var second = await AdministratorBootstrapper.EnsureAsync(db, users, email, TimeProvider.System);

        second.Should().Be(BootstrapOutcome.AlreadyAdministrator);
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
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        await AdministratorBootstrapper.EnsureAsync(db, users, email, TimeProvider.System);
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
        adopted.Role.Should().Be(UserRole.Administrator);
        adopted.Passkeys.Should().HaveCount(1);

        // And it is no longer adoptable: a real account must never be taken over by a second signup.
        var stillAdoptable = await db.Users.Include(u => u.Passkeys)
            .AnyAsync(u => u.Email == email && u.ControlWordHash == string.Empty && !u.Passkeys.Any());
        stillAdoptable.Should().BeFalse();
    }

    /// <summary>
    /// The renamed configuration key (P1T-236). <c>IConfiguration</c> answers a key nobody writes
    /// with null, so an environment still setting <c>Auth:SeedServiceManagerEmail</c> would boot
    /// clean with no Administrator and no error — the failure would surface as an operator who
    /// cannot reach the roster. The host refuses to start instead.
    /// </summary>
    [Fact]
    public void The_retired_seed_key_stops_the_host_from_starting()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:SeedServiceManagerEmail", "operator@example.com"));

        var boot = () => host.CreateClient();

        boot.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:SeedAdministratorEmail*");
    }

    /// <summary>Empty is still somebody's deliberate setting, and reading as "absent" would let the
    /// one deployment that turned the bootstrap off on purpose through unrenamed.</summary>
    [Fact]
    public void An_empty_retired_key_is_refused_too()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:SeedServiceManagerEmail", string.Empty));

        var boot = () => host.CreateClient();

        boot.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task An_unconfigured_bootstrap_does_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        var outcome = await AdministratorBootstrapper.EnsureAsync(db, users, "   ", TimeProvider.System);

        outcome.Should().Be(BootstrapOutcome.NotConfigured);
    }
}
