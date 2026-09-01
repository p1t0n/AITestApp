using System.Text;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Wires passwordless auth for the Web host: WebAuthn (fido2-net-lib) for ceremonies, a challenge
/// store, the session-token issuer, and JWT bearer validation. The Agents service mirrors only the
/// validation half (<c>ExpertToJob.Agents.Auth.SessionAuthExtensions</c>) using the same key.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddPasskeyAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AuthOptions>()
            .Bind(config.GetSection(AuthOptions.Section));

        var auth = config.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();

        services.AddFido2(options =>
        {
            options.ServerDomain = auth.Passkey.ServerDomain;
            options.ServerName = auth.Passkey.ServerName;
            options.Origins = new HashSet<string>(auth.Passkey.Origins);
            options.TimestampDriftTolerance = 300_000;
        });

        // In-memory distributed cache backs the single-use ceremony challenge store. Swap for Redis
        // when the app runs multi-instance.
        services.AddDistributedMemoryCache();
        services.AddSingleton<IChallengeStore, DistributedCacheChallengeStore>();
        services.AddSingleton<IControlWordHasher, ControlWordHasher>();
        services.AddScoped<IJwtTokenIssuer, JwtTokenIssuer>();

        AddSessionJwtAuthentication(services, auth.Jwt);

        return services;
    }

    /// <summary>
    /// Registers JWT bearer validation for the shared session token. Kept as a discrete method so
    /// the validation parameters stay in one readable place; the Agents service uses the identical
    /// parameters (same signing key, issuer, audience) against its own configuration.
    /// </summary>
    public static void AddSessionJwtAuthentication(IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // No inbound claim-type remapping: the token says "role" and "sub", and that is
                // what the app reads. The legacy WS-* mapping would silently rename both.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = SessionClaims.Subject,
                    RoleClaimType = SessionClaims.Role,
                };

                // Revocation. A signature and a lifetime only say the token was minted here and
                // has not expired; whether the session is still *current* is a fact about the
                // account, re-read on every request. The check itself is shared with the Agents
                // host (ExpertToJob.Application.Auth.SessionRevocation) so the two cannot drift.
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

        // Default-deny, and staff-by-default. The fallback policy covers an endpoint that declares
        // no authorization at all; the default policy covers a bare [Authorize]. Both are
        // ServiceManager, so an endpoint added later is closed to Experts until someone opts it in
        // with [Authorize(Policy = AuthPolicies.Expert)]. The structural endpoint-classification
        // test refuses a controller that leaves the audience implicit.
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
    }
}
