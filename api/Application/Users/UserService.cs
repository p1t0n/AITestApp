using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Users;

/// <summary>
/// User management. A Service Manager surface end to end (P1T-181): the controller above it is
/// staff-only wholesale, because token caps and account status are staffing decisions an Expert must
/// not make for themselves. Ownership scoping — "your own row only" — is a separate concern and
/// arrives with the Expert's own account surface. Not exposed over MCP.
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<UserDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default);

    /// <summary>
    /// Moves one account between roles, and the only place that write happens (P1T-238) — the
    /// startup bootstrap promotes through here too, so "set the role, bump the version" exists
    /// once.
    /// </summary>
    /// <param name="actingUserId">Who is doing it. Passed in rather than read from an ambient
    /// current-user service, so the Application layer stays a function of its arguments and the
    /// self-change rule is testable without a request.</param>
    Task<UserDetailDto> ChangeRoleAsync(
        Guid id, UserRole role, Guid actingUserId, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class UserService(IAppDbContext db) : IUserService
{
    public async Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserSummaryDto(
                u.Id, u.Email, u.Role, u.Status,
                u.DailyTokenCap, u.WeeklyTokenCap, u.MonthlyTokenCap,
                u.Passkeys.Count, u.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<UserDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDetailDto(
                u.Id, u.Email, u.Role, u.Status,
                u.DailyTokenCap, u.WeeklyTokenCap, u.MonthlyTokenCap,
                u.Passkeys.Count, u.CreatedAt, u.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), id);
    }

    public async Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default)
    {
        Validate(dto);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(User), id);

        var email = dto.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email == email, ct))
        {
            throw new ConflictException("Another account already uses this email.");
        }

        user.Email = email;
        user.Status = dto.Status;
        user.DailyTokenCap = dto.DailyTokenCap;
        user.WeeklyTokenCap = dto.WeeklyTokenCap;
        user.MonthlyTokenCap = dto.MonthlyTokenCap;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<UserDetailDto> ChangeRoleAsync(
        Guid id, UserRole role, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(User), id);

        // Nobody edits their own privilege. An Administrator who demotes themselves loses the very
        // surface they would need to undo it.
        if (id == actingUserId)
        {
            throw new ConflictException("You cannot change your own role.");
        }

        // And the guard the first one cannot give: two Administrators demoting each other empties
        // the role one legal step at a time, leaving an installation nobody can administer.
        if (user.Role == UserRole.Administrator && role != UserRole.Administrator
            && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Administrator, ct))
        {
            throw new ConflictException("The last Administrator cannot be demoted.");
        }

        // Re-selecting the value somebody already has is not a privilege change. Writing anyway
        // would bump the token version and sign them out of a form that did nothing.
        if (user.Role == role)
        {
            return await GetAsync(id, ct);
        }

        user.Role = role;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        // The role travels in the session token, so the sessions minted under the old one have to
        // stop working — otherwise a demotion takes effect whenever the person next signs in.
        user.TokenVersion++;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(User), id);

        // Passkeys cascade-delete with the user (configured in AppDbContext).
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(UpdateUserDto dto)
    {
        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains('@'))
        {
            failures.Add(new ValidationFailure(nameof(dto.Email), "A valid email is required."));
        }

        foreach (var (name, value) in new[]
        {
            (nameof(dto.DailyTokenCap), dto.DailyTokenCap),
            (nameof(dto.WeeklyTokenCap), dto.WeeklyTokenCap),
            (nameof(dto.MonthlyTokenCap), dto.MonthlyTokenCap),
        })
        {
            if (value is < 0)
            {
                failures.Add(new ValidationFailure(name, "Token cap cannot be negative."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
