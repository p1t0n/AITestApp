using System.Text;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ExpertToJob.Agents.Auth;

/// <summary>
/// Validates the shared session JWT issued by the Web host. The Agents service is a separate
/// process that does not reference the Web project, so the validation parameters are duplicated
/// here — they MUST stay in sync with ExpertToJob.Web.Auth (same signing key, issuer, audience,
/// read from the same "Auth:Jwt" configuration section). Validation only; the Agents service never
/// issues session tokens.
///
/// <para>Two things are deliberately *not* duplicated: the claim names and the revocation rule.
/// Both live in <c>ExpertToJob.Application.Auth</c>, which both hosts reference, because a token
/// version this host forgot to check would leave the agent surface reachable by a revoked session
/// long after the Web API had shut it out.</para>
/// </summary>
public static class SessionAuthExtensions
{
    public static IServiceCollection AddSessionJwtAuthentication(
        this IServiceCollection services, IConfiguration config)
    {
        var signingKey = config["Auth:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Auth:Jwt:SigningKey is not configured.");
        var issuer = config["Auth:Jwt:Issuer"] ?? "experttojob";
        var audience = config["Auth:Jwt:Audience"] ?? "experttojob-app";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
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
                    NameClaimType = SessionClaims.Subject,
                    RoleClaimType = SessionClaims.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
                        var reason = await SessionRevocation.CheckAsync(
                            db, context.Principal!, context.HttpContext.RequestAborted);
                        if (reason is not null)
                        {
                            context.Fail(reason);
                        }
                    },
                };
            });

        // Same default-deny as the Web host: the agent surfaces are staff surfaces today, and every
        // one of them declares a bare .RequireAuthorization(), which resolves to the default policy.
        // Making that policy ServiceManager closes the whole surface to Experts in one place —
        // including an endpoint added later, until it opts in explicitly.
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.ServiceManager, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthPolicies.ServiceManager));

            options.AddPolicy(AuthPolicies.Expert, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthPolicies.Expert));

            options.DefaultPolicy = options.GetPolicy(AuthPolicies.ServiceManager)!;
            options.FallbackPolicy = options.DefaultPolicy;
        });

        return services;
    }
}
