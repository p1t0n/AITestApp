using ExpertToJob.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The audit. Since P1T-181 the Web API has two audiences, and the difference between them is the
/// difference between staff data and one person's own data — so "who is this endpoint for?" must be
/// answered on the endpoint, in the source, and not inferred from the fallback policy.
///
/// <para>This walks the host's real <see cref="EndpointDataSource"/> rather than a hand-kept list,
/// because a list is a thing you forget to add to. Add a controller action without declaring its
/// audience and this test fails, naming it — the endpoint is closed either way (the fallback policy
/// is ServiceManager), but an implicit audience is how the next Expert-reachable endpoint quietly
/// becomes a staff endpoint, or worse.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class EndpointClassificationTests(WebApiFactory factory)
{
    private static readonly string[] Audiences = [AuthPolicies.ServiceManager, AuthPolicies.Expert];

    [Fact]
    public void Every_endpoint_declares_its_audience()
    {
        var unclassified = Endpoints()
            .Where(endpoint => Classify(endpoint) is null)
            .Select(Describe)
            .ToList();

        unclassified.Should().BeEmpty(
            "every endpoint must declare its audience explicitly — [Authorize(Policy = " +
            "AuthPolicies.ServiceManager)], [Authorize(Policy = AuthPolicies.Expert)], or a " +
            "deliberate [AllowAnonymous]. Unclassified: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void The_audit_sees_the_real_route_table()
    {
        // Keeps the check above honest: an empty (or nearly empty) endpoint list would pass it
        // silently. The roster, the catalog, user administration and the auth ceremonies are all
        // controllers, so the real table is comfortably larger than this floor.
        Endpoints().Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void The_auth_ceremonies_are_the_only_anonymous_endpoints()
    {
        // Anonymous is the one classification that cannot be undone by a policy, so it gets named
        // rather than merely counted: a new anonymous endpoint has to be a deliberate edit here.
        var anonymous = Endpoints()
            .Where(e => Classify(e) == "Anonymous")
            .Select(Route)
            .ToList();

        anonymous.Should().OnlyContain(route => route.StartsWith("api/auth/"));
    }

    /// <summary>
    /// What the endpoint says about its audience, or null when it says nothing. Deliberately reads
    /// only metadata a developer wrote on the endpoint — the fallback policy is invisible here on
    /// purpose, because "closed because nobody said otherwise" is not a declaration.
    /// </summary>
    private static string? Classify(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return "Anonymous";
        }

        var policies = endpoint.Metadata
            .OfType<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => p is not null)
            .ToList();

        return Audiences.FirstOrDefault(audience => policies.Contains(audience));
    }

    private static string Describe(Endpoint endpoint) => endpoint.DisplayName ?? endpoint.ToString() ?? "(unnamed)";

    /// <summary>The endpoint's route template, which is what an anonymous surface is judged by.</summary>
    private static string Route(Endpoint endpoint) =>
        endpoint is RouteEndpoint route ? route.RoutePattern.RawText ?? "" : Describe(endpoint);

    private IReadOnlyList<Endpoint> Endpoints()
    {
        // The host registers a composite source plus one per data source; flatten them all so the
        // audit cannot miss a table registered on the side.
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Concat(factory.Services.GetService<EndpointDataSource>()?.Endpoints ?? [])
            .Distinct()
            .ToList();

        endpoints.Should().NotBeEmpty("the audit is meaningless if it cannot see the route table");
        return endpoints;
    }
}
