using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The boots-without-OTLP invariant (P1T-214): a service started on its own, with no OTLP endpoint
/// anywhere, must boot and serve.
/// </summary>
/// <remarks>
/// <para>These build a real host over a real Kestrel socket rather than going through
/// <c>WebApplicationFactory&lt;Program&gt;</c> like the host test projects do: a library has no
/// <c>Program</c>, and the claim under test is that the process starts and answers — which a test
/// server that never binds a socket cannot make.</para>
/// <para>The tests set and restore <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> on the process, so they live
/// in one class on purpose: xUnit runs the tests of a single class one at a time, and a second
/// class mutating the same variable in parallel would make both flaky.</para>
/// </remarks>
public sealed class ServiceDefaultsBootTests
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public async Task Boots_and_serves_health_with_no_otlp_endpoint_set()
    {
        await using var host = await StartAsync(otlpEndpoint: null);

        var response = await host.Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Serves_the_liveness_endpoint_with_no_otlp_endpoint_set()
    {
        await using var host = await StartAsync(otlpEndpoint: null);

        var response = await host.Client.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    /// <summary>
    /// The other half of the invariant: an endpoint that is configured but has nothing listening is
    /// what every developer running a host without the dashboard actually has, and it must not fail
    /// the boot either. This is the behaviour the two hand-rolled pipelines had unconditionally.
    /// </summary>
    [Fact]
    public async Task Boots_and_serves_when_the_otlp_endpoint_points_at_nothing()
    {
        await using var host = await StartAsync(otlpEndpoint: "http://127.0.0.1:4317");

        var response = await host.Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Adoption is by composition: a host that extends the shared pipeline with its own
    /// <c>AddSource</c> keeps that subscription. Losing one is a silent telemetry regression, not a
    /// build error, which is why it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task A_source_added_after_the_shared_pipeline_is_still_subscribed()
    {
        const string hostSource = "ExpertToJob.ServiceDefaults.Tests.HostOwnedSource";
        const string neverRegistered = "ExpertToJob.ServiceDefaults.Tests.NeverRegisteredSource";

        await using var host = await StartAsync(
            otlpEndpoint: null,
            configure: builder => builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddSource(hostSource)));

        using var subscribed = new ActivitySource(hostSource);
        using var unsubscribed = new ActivitySource(neverRegistered);

        subscribed.HasListeners().Should().BeTrue();
        unsubscribed.HasListeners().Should().BeFalse("a listener for every source would prove nothing");
    }

    private static async Task<RunningHost> StartAsync(
        string? otlpEndpoint,
        Action<WebApplicationBuilder>? configure = null)
    {
        var previous = Environment.GetEnvironmentVariable(OtlpEndpointVariable);
        Environment.SetEnvironmentVariable(OtlpEndpointVariable, otlpEndpoint);

        try
        {
            var builder = WebApplication.CreateBuilder();
            // Port 0: the OS picks a free one, so a developer already running the stack does not
            // fail this suite with an address-in-use.
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.AddServiceDefaults();
            configure?.Invoke(builder);

            var app = builder.Build();
            app.MapDefaultEndpoints();
            await app.StartAsync();

            return new RunningHost(app);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OtlpEndpointVariable, previous);
        }
    }

    private sealed class RunningHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
