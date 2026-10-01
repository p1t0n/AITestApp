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
    DateTimeOffset? HiddenAt = null,
    /// <summary>When this person last did something with their own record (P1T-188). Omitted when
    /// they never have, which is the ordinary state of an unclaimed record — the retention clock
    /// then runs from collection instead.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? LastActivityAt = null);

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

// ---- Bench listing (EXP-94) ----

/// <summary>
/// What a caller wants out of the bench listing (EXP-94): an optional case-insensitive
/// <em>substring</em> of the location, an optional case-insensitive substring of the status name,
/// and whether drafts are in. Beside <see cref="RosterQuery"/> rather than folded into it — that
/// one is the staff roster's paged, faceted screen and its <c>Locations</c> are exact matches
/// chosen from a facet list; this one is the flat bench list an agent asks "how many in Warsaw"
/// of, and a person typing "warsaw" has no facet list in front of them.
///
/// <para>Every field is optional, so a caller that asks for nothing gets exactly what
/// <c>expert_list</c> has always returned. The filters live here, in the Application layer, so
/// REST and MCP narrow identically rather than only whichever shell happened to implement
/// it.</para>
/// </summary>
/// <param name="Location">Case-insensitive <em>contains</em> over the expert's location. A row
/// with no location never matches a non-empty needle.</param>
/// <param name="Status">Case-insensitive <em>contains</em> over the status name
/// (<see cref="ExpertStatus"/>). A needle matching no status name matches nobody — a misspelling
/// is answered with zero rows rather than with the whole bench.</param>
/// <param name="IncludeDrafts">Drafts opt in (review surfaces), as in the unfiltered listing.</param>
public sealed record ExpertListQuery(
    string? Location = null,
    string? Status = null,
    bool IncludeDrafts = false);

/// <summary>
/// The bench listing's result: the matching rows, and how many there are.
///
/// <para><c>Total</c> is counted in SQL over the same filtered, visibility-scoped query the rows
/// come from, so "how many experts are in Warsaw" is answered by a number the caller can read
/// without counting a list — which is the whole point when the reader is a model with a tool-result
/// budget and a 500-row dump is refused before it ever sees it (EXP-94).</para>
/// </summary>
public sealed record ExpertListResult(int Total, IReadOnlyList<ExpertSummaryDto> Items);
