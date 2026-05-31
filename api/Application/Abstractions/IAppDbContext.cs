using EmployeeManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Application.Abstractions;

/// <summary>
/// Persistence seam the Application layer depends on. Implemented by Infrastructure's
/// AppDbContext and substitutable (e.g. EF InMemory) in tests. Lets the future MCP server
/// reuse Application services without referencing Infrastructure directly.
/// </summary>
public interface IAppDbContext
{
    DbSet<Employee> Employees { get; }
    DbSet<SpokenLanguage> SpokenLanguages { get; }
    DbSet<AvailabilityEntry> AvailabilityEntries { get; }
    DbSet<Category> Categories { get; }
    DbSet<Skill> Skills { get; }
    DbSet<EmployeeSkill> EmployeeSkills { get; }
    DbSet<Qualification> Qualifications { get; }
    DbSet<Experience> Experiences { get; }
    DbSet<Achievement> Achievements { get; }
    DbSet<ExperienceSkill> ExperienceSkills { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
