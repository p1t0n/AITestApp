using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// The first Service Manager. Signup is open but self-serve accounts are Experts, so a fresh
/// database would otherwise have nobody who can reach the roster — and no way to make one, since
/// promotion is itself a staff action. The chicken-and-egg is cut by configuration:
/// <c>Auth:SeedServiceManagerEmail</c> names the account, and startup makes it staff.
///
/// <para>Two cases, one outcome. The email already has an account → it is promoted. It does not →
/// an <em>invite</em> row is created: an account with no passkey and no control word, which cannot
/// be signed into. Signup then adopts that row instead of refusing the address as taken (see
/// <c>AuthController</c>), so the operator enrols their own passkey and lands as staff.</para>
///
/// <para>Idempotent: running it again on an already-promoted account changes nothing.</para>
/// </summary>
public static class ServiceManagerBootstrapper
{
    /// <summary>
    /// Ensures the configured email is a Service Manager. Returns what happened, so startup can
    /// log it — a promotion is a privilege change and should not be silent.
    /// </summary>
    public static async Task<BootstrapOutcome> EnsureAsync(
        DbContext db, string? email, TimeProvider clock, CancellationToken ct = default)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return BootstrapOutcome.NotConfigured;
        }

        var users = db.Set<User>();
        var existing = await users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (existing is null)
        {
            var now = clock.GetUtcNow();
            users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                // No credential and no recovery secret: this row is an invitation, not a login.
                // Signup fills both in when the operator enrols their passkey.
                ControlWordHash = string.Empty,
                Role = UserRole.ServiceManager,
                Status = UserStatus.Active,
                TokenVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            return BootstrapOutcome.Invited;
        }

        if (existing.Role == UserRole.ServiceManager)
        {
            return BootstrapOutcome.AlreadyServiceManager;
        }

        existing.Role = UserRole.ServiceManager;
        existing.UpdatedAt = clock.GetUtcNow();
        // The role travels in the token, so a live Expert session would keep its old claim.
        // Bumping the version forces a fresh sign-in and with it a token that says ServiceManager.
        existing.TokenVersion++;
        await db.SaveChangesAsync(ct);
        return BootstrapOutcome.Promoted;
    }
}

/// <summary>What <see cref="ServiceManagerBootstrapper.EnsureAsync"/> did.</summary>
public enum BootstrapOutcome
{
    /// <summary>No email configured — the bootstrap is off.</summary>
    NotConfigured,

    /// <summary>An invite row was created; it awaits the operator's passkey.</summary>
    Invited,

    /// <summary>An existing Expert account was promoted, and its sessions revoked.</summary>
    Promoted,

    /// <summary>Nothing to do.</summary>
    AlreadyServiceManager,
}
