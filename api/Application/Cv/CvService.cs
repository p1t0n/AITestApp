using System.Globalization;
using EmployeeManager.Application.Employees;
using EmployeeManager.Domain.Enums;

namespace EmployeeManager.Application.Cv;

public interface ICvService
{
    Task<CvDto> BuildAsync(Guid employeeId, CancellationToken ct = default);
}

/// <summary>
/// Assembles a CV from an employee's full record. Pure projection over the detail DTO —
/// no persistence — so it is trivially unit-testable and reusable headlessly later.
/// </summary>
public class CvService : ICvService
{
    private readonly IEmployeeService _employees;
    public CvService(IEmployeeService employees) => _employees = employees;

    public async Task<CvDto> BuildAsync(Guid employeeId, CancellationToken ct = default)
    {
        var e = await _employees.GetAsync(employeeId, ct);
        return Build(e);
    }

    public static CvDto Build(EmployeeDetailDto e)
    {
        var skillGroups = e.Skills
            .GroupBy(s => s.CategoryName)
            .OrderBy(g => g.Key)
            .Select(g => new CvSkillGroupDto(
                g.Key,
                g.OrderByDescending(s => s.Level).ThenBy(s => s.SkillName).ToList()))
            .ToList();

        var experiences = e.Experiences
            .OrderByDescending(x => x.StartDate)
            .Select(x => new CvExperienceDto(
                x.Company, x.Title, x.Location,
                FormatPeriod(x.StartDate, x.EndDate),
                x.Summary,
                x.Achievements.Select(a => a.Text).ToList(),
                x.Skills.Select(s => s.SkillName).ToList()))
            .ToList();

        return new CvDto(
            FullName: $"{e.FirstName} {e.LastName}",
            Title: e.Title,
            Email: e.Email,
            Phone: e.Phone,
            Location: e.Location,
            Summary: e.Summary,
            PhotoUrl: e.PhotoUrl,
            Availability: new CvAvailabilityDto(e.CurrentCapacityPercent, e.AvailabilityEntries),
            SkillGroups: skillGroups,
            Languages: e.SpokenLanguages,
            Experiences: experiences,
            Education: e.Qualifications.Where(q => q.Type == QualificationType.Degree).ToList(),
            Certifications: e.Qualifications.Where(q => q.Type == QualificationType.Certification).ToList());
    }

    private static string FormatPeriod(DateOnly start, DateOnly? end)
    {
        var s = start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var e = end?.ToString("MMM yyyy", CultureInfo.InvariantCulture) ?? "Present";
        return $"{s} – {e}";
    }
}
