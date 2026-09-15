using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Users;

/// <summary>Row in the user-management list.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    UserRole Role,
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
    UserRole Role,
    UserStatus Status,
    long? DailyTokenCap,
    long? WeeklyTokenCap,
    long? MonthlyTokenCap,
    int PasskeyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Editable user fields. The role is deliberately absent and stays absent: a privilege must not be
/// able to change as a side effect of editing an email or a token cap, so it moves through
/// <see cref="ChangeRoleDto"/> and its own endpoint instead (P1T-229).
///
/// <para>Caps are nullable — null means "inherit the system default" (the token caps epic reads
/// these). Email and status are always set; the control word and passkeys are not editable here
/// (recovery/own-device flows own those).</para>
/// </summary>
public sealed record UpdateUserDto(
    string Email,
    UserStatus Status,
    long? DailyTokenCap,
    long? WeeklyTokenCap,
    long? MonthlyTokenCap);

/// <summary>The one field <c>PUT /api/users/{id}/role</c> takes. A body rather than a route segment
/// so the value is typed, and a record of one so adding a second field is a visible decision.</summary>
public sealed record ChangeRoleDto(UserRole Role);
