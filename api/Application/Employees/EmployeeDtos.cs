using EmployeeManager.Domain.Enums;

namespace EmployeeManager.Application.Employees;

// ---- Read DTOs ----

public record EmployeeSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Title,
    string? Location,
    string Email,
    int CurrentCapacityPercent);

public record EmployeeDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Title,
    string Email,
    string? Phone,
    string? Location,
    string? Summary,
    string? PhotoUrl,
    int CurrentCapacityPercent,
    IReadOnlyList<SpokenLanguageDto> SpokenLanguages,
    IReadOnlyList<AvailabilityEntryDto> AvailabilityEntries,
    IReadOnlyList<EmployeeSkillDto> Skills,
    IReadOnlyList<QualificationDto> Qualifications,
    IReadOnlyList<ExperienceDto> Experiences);

public record SpokenLanguageDto(Guid Id, string Language, LanguageLevel Level);

public record AvailabilityEntryDto(Guid Id, DateOnly EffectiveFrom, int CapacityPercent);

public record EmployeeSkillDto(
    Guid Id, Guid SkillId, string SkillName, string CategoryName, SkillLevel Level, decimal YearsExperience);

public record QualificationDto(
    Guid Id,
    QualificationType Type,
    string Name,
    string? Institution,
    string? Field,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Issuer,
    string? CredentialId,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate);

public record AchievementDto(Guid Id, int Order, string Text);

public record ExperienceSkillDto(Guid Id, Guid SkillId, string SkillName);

public record ExperienceDto(
    Guid Id,
    string Company,
    string Title,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Summary,
    IReadOnlyList<AchievementDto> Achievements,
    IReadOnlyList<ExperienceSkillDto> Skills);

// ---- Write DTOs (root fields only; children managed via their own sub-resources) ----

public record SaveEmployeeDto(
    string FirstName,
    string LastName,
    string Title,
    string Email,
    string? Phone,
    string? Location,
    string? Summary,
    string? PhotoUrl);
