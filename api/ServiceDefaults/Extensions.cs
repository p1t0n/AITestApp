using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The one telemetry and health spine the Web, MCP and Agents hosts share (P1T-214).
/// </summary>
/// <remarks>
/// Adopted by <em>composition</em>, not replacement. This project owns the OTLP exporter, the
/// generic AspNetCore/HttpClient/Runtime instrumentation, HTTP resilience and the health
/// endpoints. Each host keeps its own <c>AddSource</c>/<c>AddMeter</c> list and its own
/// <c>ConfigureResource(r =&gt; r.AddService("experttojob-…"))</c> next to the code that emits
/// them — <c>AddOpenTelemetry()</c> is additive, so a host calling it again after
/// <see cref="AddServiceDefaults"/> extends the same pipeline rather than building a second one.
/// Dropping the template in as-is would silently stop collecting every gen_ai span, every MAF
/// workflow span and every MCP RPC while still looking like it worked.
/// </remarks>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string LiveTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Only attaches to IHttpClientFactory clients. The Gemini and MCP clients are both
            // hand-built `new HttpClient`, so no model call is ever retried by this; the one
            // factory client in the stack is the Keycloak token provider, where retrying is
            // desirable (manuals/adr-aspire-apphost.md).
            http.AddStandardResilienceHandler();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // Boots without OTLP: no endpoint configured, no exporter registered. Before this the two
        // hosts exported unconditionally and dropped the spans when nothing listened; both boot,
        // this one is quieter. The invariant has its own test rather than resting on this guard.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // A liveness check that says the process answers, and claims nothing else.
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (every check) and <c>/alive</c> (liveness only).
    /// </summary>
    /// <remarks>
    /// Both are anonymous on purpose: an orchestrator has no session token, and the Agents host
    /// runs a staff-only fallback policy — without this the probe would 401 and the service would
    /// look dead.
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
        }).AllowAnonymous();

        return app;
    }
}
