using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Cv;
using ExpertToJob.Infrastructure.Documents;
using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=experttojob;Username=postgres;Password=postgres";

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Stateless and thread-safe — one instance serves every request.
        services.AddSingleton<ICvPdfRenderer, CvPdfRenderer>();

        return services;
    }
}
