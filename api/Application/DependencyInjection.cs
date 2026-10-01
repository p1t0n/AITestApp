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
        services.AddScoped<Compliance.IAccessAndExportService, Compliance.AccessAndExportService>();
        services.AddScoped<Compliance.IContestService, Compliance.ContestService>();
        // The same class behind both seams — one place computes a record's sequence.
        services.AddScoped<IOwnershipChangeRecorder>(sp =>
            (ProcessingRecordService)sp.GetRequiredService<IProcessingRecordService>());
        services.AddScoped<Claims.IClaimService, Claims.ClaimService>();
        services.AddScoped<Visibility.IExpertVisibilityService, Visibility.ExpertVisibilityService>();

        // Fails closed: a host that does not say what it is looking at the roster for gets the
        // narrower answer, so a forgotten registration hides paused people rather than exposing
        // them (P1T-185). The Web host overrides this with the administration provider.
        services.TryAddSingleton<Visibility.IRosterAudienceProvider, Visibility.BenchAudienceProvider>();
        services.AddScoped<Search.IExpertDigestService, Search.ExpertDigestService>();
        services.AddScoped<Search.IExpertFilterService, Search.ExpertFilterService>();

        // Art. 15(1)(c) names the chat provider to a data subject (EXP-21). TryAdd so the host that
        // actually serves the access view wins with the configured value; the default here names
        // nothing, which the disclosure answers by naming every recipient it might be. A host that
        // forgets therefore over-tells somebody rather than quietly printing the wrong name at them.
        services.TryAddSingleton(new Compliance.ChatProviderDisclosure(null));

        // The other half of the same disclosure (EXP-66): chat and embeddings move independently,
        // so each is read from its own key and defaults here to "we do not know" for the same
        // reason — a host that forgets over-tells somebody rather than naming the wrong company.
        services.TryAddSingleton(new Compliance.EmbeddingsProviderDisclosure(null));

        // Reads the provider history the MCP host writes. Scoped because it reads through the
        // DbContext; the access view is the only caller.
        services.AddScoped<Compliance.EmbeddingsProviderHistory>();

        // Every host that composes the Application layer needs a clock now that lawful-basis
        // records are timestamped, and only two of the three registered one. TryAdd so a host that
        // supplies its own (a test's fake clock) still wins.
        services.TryAddSingleton(TimeProvider.System);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
