using EmployeeManager.Application.Employees;

namespace EmployeeManager.Application.Cv;

/// <summary>CV-shaped projection of an employee: a full dump in a fixed, render-ready order.</summary>
public record CvDto(
    string FullName,
    string Title,
    string Email,
    string? Phone,
    string? Location,
    string? Summary,
    string? PhotoUrl,
    CvAvailabilityDto Availability,
    IReadOnlyList<CvSkillGroupDto> SkillGroups,
    IReadOnlyList<SpokenLanguageDto> Languages,
    IReadOnlyList<CvExperienceDto> Experiences,
    IReadOnlyList<QualificationDto> Education,
    IReadOnlyList<QualificationDto> Certifications);

public record CvAvailabilityDto(int CurrentCapacityPercent, IReadOnlyList<AvailabilityEntryDto> Schedule);

public record CvSkillGroupDto(string Category, IReadOnlyList<EmployeeSkillDto> Skills);

public record CvExperienceDto(
    string Company,
    string Title,
    string? Location,
    string Period,
    string? Summary,
    IReadOnlyList<string> Achievements,
    IReadOnlyList<string> Skills);
