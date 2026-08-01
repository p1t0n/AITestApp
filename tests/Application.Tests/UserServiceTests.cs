using CvManager.Application.Common;
using CvManager.Application.Users;
using CvManager.Domain.Entities;
using CvManager.Domain.Enums;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CvManager.Application.Tests;

public class UserServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .Options);

    private static async Task<User> SeedUser(AppDbContext db, string email, bool withPasskey = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            ControlWordHash = "hash",
            Status = UserStatus.Active,
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
