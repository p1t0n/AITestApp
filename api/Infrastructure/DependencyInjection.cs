using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Cv;
using ExpertToJob.Infrastructure.Documents;
using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=experttojob;Username=postgres;Password=postgres";

        // Interceptors are resolved from the container so a host can add its own — the Web host
        // adds the one that stamps an Expert's own activity (P1T-188); the MCP and Agents hosts
        // deliberately add none, because an agent's write must never look like the person's.
        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
                .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Stateless and thread-safe — one instance serves every request.
        services.AddSingleton<ICvPdfRenderer, CvPdfRenderer>();

        return services;
    }
}
