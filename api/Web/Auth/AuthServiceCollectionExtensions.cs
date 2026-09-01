using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Wires passwordless auth for the Web host: WebAuthn (fido2-net-lib) for ceremonies, a challenge
/// store, the session-token issuer, and JWT bearer validation. The Agents service mirrors only the
/// validation half (<see cref="AddSessionJwtAuthentication"/>'s equivalent) using the same key.
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
                };
            });

        // Gate the whole app: every endpoint requires an authenticated user unless it opts out with
        // [AllowAnonymous] (the auth ceremonies do). The SPA enforces the same rule client-side.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
    }
}
