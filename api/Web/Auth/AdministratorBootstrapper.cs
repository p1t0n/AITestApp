using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Users;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// The first Administrator. Signup is open but self-serve accounts are Users, so a fresh database
/// would otherwise have nobody who can reach the roster — and no way to make one, since promotion
/// is itself a staff action. The chicken-and-egg is cut by configuration:
/// <c>Auth:SeedAdministratorEmail</c> names the account, and startup makes it staff.
///
/// <para>Two cases, one outcome. The email already has an account → it is promoted. It does not →
/// an <em>invite</em> row is created: an account with no passkey and no control word, which cannot
/// be signed into. Signup then adopts that row instead of refusing the address as taken (see
/// <c>AuthController</c>), so the operator enrols their own passkey and lands as staff.</para>
///
/// <para>Idempotent: running it again on an already-promoted account changes nothing.</para>
///
/// <para>The promotion itself goes through <see cref="IUserService.ChangeRoleAsync"/> (P1T-238), so
/// "set the role, bump the token version" exists in exactly one place. Both of that method's
/// refusals are unreachable from here by construction: this only ever promotes, and it runs at
/// startup with nobody signed in to be the acting account.</para>
/// </summary>
public static class AdministratorBootstrapper
{
    /// <summary>
    /// Ensures the configured email is an Administrator. Returns what happened, so startup can
    /// log it — a promotion is a privilege change and should not be silent.
    /// </summary>
    public static async Task<BootstrapOutcome> EnsureAsync(
        IAppDbContext db, IUserService users, string? email, TimeProvider clock,
        CancellationToken ct = default)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return BootstrapOutcome.NotConfigured;
        }

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (existing is null)
        {
            var now = clock.GetUtcNow();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                // No credential and no recovery secret: this row is an invitation, not a login.
                // Signup fills both in when the operator enrols their passkey.
                ControlWordHash = string.Empty,
                Role = UserRole.Administrator,
                Status = UserStatus.Active,
                TokenVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            return BootstrapOutcome.Invited;
        }

        if (existing.Role == UserRole.Administrator)
        {
            return BootstrapOutcome.AlreadyAdministrator;
        }

        // No acting account exists at startup, so the id that would trigger the self-change refusal
        // is one no row can have. The write and the token-version bump live in the service.
        await users.ChangeRoleAsync(existing.Id, UserRole.Administrator, Guid.Empty, ct);
        return BootstrapOutcome.Promoted;
    }
}

/// <summary>What <see cref="AdministratorBootstrapper.EnsureAsync"/> did.</summary>
public enum BootstrapOutcome
{
    /// <summary>No email configured — the bootstrap is off.</summary>
    NotConfigured,

    /// <summary>An invite row was created; it awaits the operator's passkey.</summary>
    Invited,

    /// <summary>An existing User account was promoted, and its sessions revoked.</summary>
    Promoted,

    /// <summary>Nothing to do.</summary>
    AlreadyAdministrator,
}
