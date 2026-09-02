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

        // Erasure is registered with the hasher rather than in AddApplication, because it depends
        // on it and this is the only host that has one: the MCP server composes the same
        // Application layer and must not fail to start over a service it can never serve
        // (P1T-186). Deleting yourself is a session act, and sessions live here.
        services.AddScoped<ExpertToJob.Application.Compliance.IErasureService,
            ExpertToJob.Application.Compliance.ErasureService>();
        services.AddScoped<IJwtTokenIssuer, JwtTokenIssuer>();

        // Row-level reach (P1T-182). The Application services ask this; the Web host answers from
        // the session. Needs the accessor because the scope is a property of the caller, not of a
        // parameter any controller could be trusted to pass down.
        services.AddHttpContextAccessor();
        services.AddScoped<IOwnershipScopeProvider, HttpOwnershipScopeProvider>();

        // What this host is looking at the roster for (P1T-185). Administration: its roster screens
        // are the bench's admin surfaces and must show a paused Expert rather than lose them, and
        // the one Expert-facing surface shows a person the row they themselves paused. The
        // availability-shaped paths — search, digests, scan enumeration — filter regardless of this.
        services.AddSingleton<ExpertToJob.Application.Visibility.IRosterAudienceProvider,
            ExpertToJob.Application.Visibility.AdministrationAudienceProvider>();

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

            // Both audiences, still explicit on the endpoint (P1T-182). Used where the row-level
            // answer comes from the ownership scope rather than from the policy: the catalog's
            // reads, an Expert's own row and its children.
            options.AddPolicy(AuthPolicies.AnyRole, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthPolicies.ServiceManager, AuthPolicies.Expert));

            options.DefaultPolicy = options.GetPolicy(AuthPolicies.ServiceManager)!;
            options.FallbackPolicy = options.DefaultPolicy;
        });
    }
}
