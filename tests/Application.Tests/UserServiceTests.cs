using ExpertToJob.Application.Common;
using ExpertToJob.Application.Users;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class UserServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .Options);

    private static async Task<User> SeedUser(AppDbContext db, string email, bool withPasskey = true,
        UserRole role = UserRole.User)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            ControlWordHash = "hash",
            Role = role,
            Status = UserStatus.Active,
            TokenVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (withPasskey)
        {
            user.Passkeys.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                CredentialId = Guid.NewGuid().ToByteArray(),
                PublicKey = [1, 2, 3],
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static UpdateUserDto Update(string email, UserStatus status = UserStatus.Active,
        long? daily = null, long? weekly = null, long? monthly = null) =>
        new(email, status, daily, weekly, monthly);

    [Fact]
    public async Task ListAsync_returns_users_ordered_by_email_with_passkey_count()
    {
        await using var db = NewDb();
        await SeedUser(db, "bob@x.com");
        await SeedUser(db, "alice@x.com");
        var svc = new UserService(db);

        var users = await svc.ListAsync();

        users.Select(u => u.Email).Should().Equal("alice@x.com", "bob@x.com");
        users[0].PasskeyCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_unknown_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = new UserService(db);

        var act = () => svc.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_changes_email_status_and_caps()
    {
        await using var db = NewDb();
        var user = await SeedUser(db, "old@x.com");
        var svc = new UserService(db);

        var result = await svc.UpdateAsync(user.Id, Update("New@X.com", UserStatus.Deactivated, daily: 1000));

        result.Email.Should().Be("new@x.com"); // normalized
        result.Status.Should().Be(UserStatus.Deactivated);
        result.DailyTokenCap.Should().Be(1000);
        result.WeeklyTokenCap.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_duplicate_email_throws_Conflict()
    {
        await using var db = NewDb();
        await SeedUser(db, "taken@x.com");
        var user = await SeedUser(db, "mine@x.com");
        var svc = new UserService(db);

        var act = () => svc.UpdateAsync(user.Id, Update("taken@x.com"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateAsync_invalid_email_throws_Validation()
    {
        await using var db = NewDb();
        var user = await SeedUser(db, "ok@x.com");
        var svc = new UserService(db);

        var act = () => svc.UpdateAsync(user.Id, Update("not-an-email"));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_negative_cap_throws_Validation()
    {
        await using var db = NewDb();
        var user = await SeedUser(db, "ok@x.com");
        var svc = new UserService(db);

        var act = () => svc.UpdateAsync(user.Id, Update("ok@x.com", daily: -5));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ListAsync_and_GetAsync_carry_the_role()
    {
        await using var db = NewDb();
        var staff = await SeedUser(db, "staff@x.com", role: UserRole.Administrator);
        await SeedUser(db, "person@x.com");
        var svc = new UserService(db);

        var listed = await svc.ListAsync();
        var one = await svc.GetAsync(staff.Id);

        listed.Single(u => u.Email == "staff@x.com").Role.Should().Be(UserRole.Administrator);
        listed.Single(u => u.Email == "person@x.com").Role.Should().Be(UserRole.User);
        one.Role.Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task ChangeRoleAsync_unknown_throws_NotFound()
    {
        await using var db = NewDb();
        var actor = await SeedUser(db, "actor@x.com", role: UserRole.Administrator);
        var svc = new UserService(db);

        var act = () => svc.ChangeRoleAsync(Guid.NewGuid(), UserRole.Administrator, actor.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// The self-demotion guard. Not a courtesy: an Administrator who demotes themselves loses the
    /// surface they would need to undo it, and on a one-administrator install that is the whole
    /// installation locked out.
    /// </summary>
    [Fact]
    public async Task ChangeRoleAsync_refuses_to_change_the_actors_own_role()
    {
        await using var db = NewDb();
        await SeedUser(db, "other-staff@x.com", role: UserRole.Administrator);
        var actor = await SeedUser(db, "actor@x.com", role: UserRole.Administrator);
        var svc = new UserService(db);

        var act = () => svc.ChangeRoleAsync(actor.Id, UserRole.User, actor.Id);

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("You cannot change your own role.");
    }

    /// <summary>
    /// And the guard that catches what the first one cannot: two Administrators demoting each other
    /// would empty the role one step at a time, with every single step legal.
    /// </summary>
    [Fact]
    public async Task ChangeRoleAsync_refuses_to_demote_the_last_administrator()
    {
        await using var db = NewDb();
        var last = await SeedUser(db, "last@x.com", role: UserRole.Administrator);
        var actor = await SeedUser(db, "actor@x.com", role: UserRole.Administrator);
        var svc = new UserService(db);

        // The actor demotes themselves out of the way first — through the column, since the service
        // is exactly what refuses to do it.
        actor.Role = UserRole.User;
        await db.SaveChangesAsync();

        var act = () => svc.ChangeRoleAsync(last.Id, UserRole.User, actor.Id);

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("The last Administrator cannot be demoted.");
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == last.Id)).Role
            .Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task ChangeRoleAsync_promotes_and_bumps_the_token_version_by_exactly_one()
    {
        await using var db = NewDb();
        var actor = await SeedUser(db, "actor@x.com", role: UserRole.Administrator);
        var subject = await SeedUser(db, "subject@x.com");
        var before = subject.UpdatedAt;
        var svc = new UserService(db);

        var result = await svc.ChangeRoleAsync(subject.Id, UserRole.Administrator, actor.Id);

        result.Role.Should().Be(UserRole.Administrator);
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subject.Id);
        stored.Role.Should().Be(UserRole.Administrator);
        stored.TokenVersion.Should().Be(2, "the old token still claims User, so it has to stop working");
        stored.UpdatedAt.Should().BeOnOrAfter(before);
    }

    /// <summary>
    /// Re-selecting the value a person already has is not a privilege change, and must not sign them
    /// out. The selector in the users dictionary makes this an easy accident.
    /// </summary>
    [Fact]
    public async Task ChangeRoleAsync_writes_nothing_when_the_role_is_unchanged()
    {
        await using var db = NewDb();
        var actor = await SeedUser(db, "actor@x.com", role: UserRole.Administrator);
        var subject = await SeedUser(db, "subject@x.com");
        var before = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subject.Id);
        var svc = new UserService(db);

        var result = await svc.ChangeRoleAsync(subject.Id, UserRole.User, actor.Id);

        result.Role.Should().Be(UserRole.User);
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == subject.Id);
        stored.TokenVersion.Should().Be(before.TokenVersion);
        stored.UpdatedAt.Should().Be(before.UpdatedAt);
    }

    /// <summary>
    /// The one refusal the last-Administrator rule must not make: promoting somebody when there is
    /// exactly one Administrator is how an install grows a second one.
    /// </summary>
    [Fact]
    public async Task ChangeRoleAsync_promotion_is_allowed_with_a_single_administrator()
    {
        await using var db = NewDb();
        var actor = await SeedUser(db, "only@x.com", role: UserRole.Administrator);
        var subject = await SeedUser(db, "subject@x.com");
        var svc = new UserService(db);

        var result = await svc.ChangeRoleAsync(subject.Id, UserRole.Administrator, actor.Id);

        result.Role.Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_user()
    {
        await using var db = NewDb();
        var user = await SeedUser(db, "gone@x.com");
        var svc = new UserService(db);

        await svc.DeleteAsync(user.Id);

        (await db.Users.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_unknown_throws_NotFound()
    {
        await using var db = NewDb();
        var svc = new UserService(db);

        var act = () => svc.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
