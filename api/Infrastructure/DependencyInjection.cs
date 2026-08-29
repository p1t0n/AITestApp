using CvManager.Application.Abstractions;
using CvManager.Application.Cv;
using CvManager.Infrastructure.Documents;
using CvManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=cvmanager;Username=postgres;Password=postgres";

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Stateless and thread-safe — one instance serves every request.
        services.AddSingleton<ICvPdfRenderer, CvPdfRenderer>();

        return services;
    }
}
