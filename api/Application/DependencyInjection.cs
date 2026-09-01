using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Skills;
using ExpertToJob.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpertToJob.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IExpertService, ExpertService>();
        services.AddScoped<IExpertSkillService, ExpertSkillService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<IQualificationService, QualificationService>();
        services.AddScoped<IExperienceService, ExperienceService>();
        services.AddScoped<IAchievementService, AchievementService>();
        services.AddScoped<IExperienceSkillService, ExperienceSkillService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<ISkillCatalogService, SkillCatalogService>();
        services.AddScoped<ICvService, CvService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProcessingRecordService, ProcessingRecordService>();
        services.AddScoped<Search.IExpertDigestService, Search.ExpertDigestService>();
        services.AddScoped<Search.IExpertFilterService, Search.ExpertFilterService>();

        // Every host that composes the Application layer needs a clock now that lawful-basis
        // records are timestamped, and only two of the three registered one. TryAdd so a host that
        // supplies its own (a test's fake clock) still wins.
        services.TryAddSingleton(TimeProvider.System);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
