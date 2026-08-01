using CvManager.Application.Availability;
using CvManager.Domain.Entities;

namespace CvManager.Application.Employees;

internal static class EmployeeMappings
{
    public static EmployeeSummaryDto ToSummary(this Employee e, DateOnly onDate) => new(
        e.Id, e.FirstName, e.LastName, e.Title, e.Location, e.Email,
        CapacityCalculator.CapacityOn(e.AvailabilityEntries, onDate));

    public static EmployeeDetailDto ToDetail(this Employee e, DateOnly onDate) => new(
        e.Id, e.FirstName, e.LastName, e.Title, e.Email, e.Phone, e.Location, e.Summary, e.PhotoUrl,
        CapacityCalculator.CapacityOn(e.AvailabilityEntries, onDate),
        e.SpokenLanguages.Select(l => new SpokenLanguageDto(l.Id, l.Language, l.Level)).ToList(),
        e.AvailabilityEntries.OrderBy(a => a.EffectiveFrom)
            .Select(a => new AvailabilityEntryDto(a.Id, a.EffectiveFrom, a.CapacityPercent)).ToList(),
        e.Skills.Select(s => new EmployeeSkillDto(
            s.Id, s.SkillId, s.Skill.Name, s.Skill.Category.Name, s.Level, s.YearsExperience)).ToList(),
        e.Qualifications.Select(q => new QualificationDto(
            q.Id, q.Type, q.Name, q.Institution, q.Field, q.StartDate, q.EndDate,
            q.Issuer, q.CredentialId, q.IssueDate, q.ExpiryDate)).ToList(),
        e.Experiences.OrderByDescending(x => x.StartDate).Select(x => x.ToDto()).ToList());

    public static ExperienceDto ToDto(this Experience x) => new(
        x.Id, x.Company, x.Title, x.Location, x.StartDate, x.EndDate, x.Summary,
        x.Achievements.OrderBy(a => a.Order).Select(a => new AchievementDto(a.Id, a.Order, a.Text)).ToList(),
        x.Skills.Select(s => new ExperienceSkillDto(s.Id, s.SkillId, s.Skill.Name)).ToList());
}
