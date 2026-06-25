using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EmployeeManager.Agents.Auth;

/// <summary>
/// Validates the shared session JWT issued by the Web host. The Agents service is a separate
/// process that does not reference the Web project, so the validation parameters are duplicated
/// here — they MUST stay in sync with EmployeeManager.Web.Auth (same signing key, issuer, audience,
/// read from the same "Auth:Jwt" configuration section). Validation only; the Agents service never
/// issues session tokens.
/// </summary>
public static class SessionAuthExtensions
{
    public static IServiceCollection AddSessionJwtAuthentication(
        this IServiceCollection services, IConfiguration config)
    {
        var signingKey = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey is not configured.");
        var issuer = config["Auth:Jwt:Issuer"] ?? "employeemanager";
        var audience = config["Auth:Jwt:Audience"] ?? "employeemanager-app";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }
}
