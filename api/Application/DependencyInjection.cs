using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Employees;
using ExpertToJob.Application.Skills;
using ExpertToJob.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeSkillService, EmployeeSkillService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<IQualificationService, QualificationService>();
        services.AddScoped<IExperienceService, ExperienceService>();
        services.AddScoped<IAchievementService, AchievementService>();
        services.AddScoped<IExperienceSkillService, ExperienceSkillService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<ISkillCatalogService, SkillCatalogService>();
        services.AddScoped<ICvService, CvService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<Search.IEmployeeDigestService, Search.EmployeeDigestService>();
        services.AddScoped<Search.IEmployeeFilterService, Search.EmployeeFilterService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
