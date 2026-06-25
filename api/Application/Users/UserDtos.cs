using EmployeeManager.Domain.Enums;

namespace EmployeeManager.Application.Users;

/// <summary>Row in the user-management list.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    UserStatus Status,
    long? DailyTokenCap,
    long? WeeklyTokenCap,
    long? MonthlyTokenCap,
    int PasskeyCount,
    DateTimeOffset CreatedAt);

/// <summary>Single user with timestamps.</summary>
public sealed record UserDetailDto(
    Guid Id,
    string Email,
    UserStatus Status,
    long? DailyTokenCap,
    long? WeeklyTokenCap,
    long? MonthlyTokenCap,
    int PasskeyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Editable user fields. Caps are nullable — null means "inherit the system default" (the token
/// caps epic reads these). Email and status are always set; the control word and passkeys are not
/// editable here (recovery/own-device flows own those).
/// </summary>
public sealed record UpdateUserDto(
    string Email,
    UserStatus Status,
    long? DailyTokenCap,
    long? WeeklyTokenCap,
    long? MonthlyTokenCap);
