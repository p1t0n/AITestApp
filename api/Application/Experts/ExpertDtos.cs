using System.Text.Json.Serialization;
using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Experts;

// ---- Read DTOs ----

public record ExpertSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Title,
    string? Location,
    string Email,
    int CurrentCapacityPercent,
    ExpertStatus Status,
    /// <summary>When this person paused themselves, or absent while they are on the bench
    /// (P1T-185). Omitted rather than serialised as null, and that is not cosmetic: this projection
    /// is what <c>expert_list</c> hands an agent on every model call, agents never see a paused
    /// Expert at all, and a null per row would be pure token cost forever.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HiddenAt = null);

public record ExpertDetailDto(
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
    ExpertStatus Status,
    IReadOnlyList<SpokenLanguageDto> SpokenLanguages,
    IReadOnlyList<AvailabilityEntryDto> AvailabilityEntries,
    IReadOnlyList<ExpertSkillDto> Skills,
    IReadOnlyList<QualificationDto> Qualifications,
    IReadOnlyList<ExperienceDto> Experiences,
    /// <inheritdoc cref="ExpertSummaryDto.HiddenAt"/>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HiddenAt = null);

public record SpokenLanguageDto(Guid Id, string Language, LanguageLevel Level);

public record AvailabilityEntryDto(Guid Id, DateOnly EffectiveFrom, int CapacityPercent);

public record ExpertSkillDto(
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

public record SaveExpertDto(
    string FirstName,
    string LastName,
    string Title,
    string Email,
    string? Phone,
    string? Location,
    string? Summary,
    string? PhotoUrl);

/// <summary>Partial update: every field is optional, and only the fields present (non-null)
/// overwrite the expert's current value — the complement to <see cref="SaveExpertDto"/>'s full
/// replace. Cannot clear an optional field to null in one call; use the full-replace path for that.</summary>
public record UpdateExpertDto(
    string? FirstName,
    string? LastName,
    string? Title,
    string? Email,
    string? Phone,
    string? Location,
    string? Summary,
    string? PhotoUrl);
