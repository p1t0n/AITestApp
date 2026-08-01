using CvManager.Application.Abstractions;
using CvManager.Application.Common;
using CvManager.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace CvManager.Application.Users;

/// <summary>
/// User management. Roles are flat — any authenticated caller may manage any user — so there is no
/// authorization beyond "signed in". Not exposed over MCP.
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<UserDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default);
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
                u.Id, u.Email, u.Status,
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
                u.Id, u.Email, u.Status,
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
