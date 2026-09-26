using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The Agents host's default-deny audit, the sibling of the Web host's
/// <c>EndpointClassificationTests</c>. The rule here is simpler because the audience is: every
/// agent surface is a staff surface, the fallback policy is Administrator, and every route says
/// <c>.RequireAuthorization()</c> out loud rather than leaning on that fallback.
///
/// <para>This walks the host's real <see cref="EndpointDataSource"/> rather than a list somebody
/// maintains, because a list is a thing you forget to add to. A route added without an
/// authorization requirement fails here, named — it is still closed by the fallback policy today,
/// but "closed because nobody said otherwise" survives exactly until someone relaxes the fallback,
/// and the conversation-history routes (EXP-33) serve one person's own transcripts.</para>
/// </summary>
public class AgentsEndpointClassificationTests
{
    [Fact]
    public void Every_route_requires_authorization_except_the_health_probes()
    {
        var open = Routes()
            .Where(r => r.Endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(r => r.Route)
            .ToList();

        // The two probes are anonymous on purpose and always were: an orchestrator has no session
        // token, and a probe that 401s makes a healthy service look dead. They carry no personal
        // data and say nothing but "Healthy".
        open.Should().OnlyContain(route => route == "/health" || route == "/alive",
            "every agent route must say .RequireAuthorization() rather than rely on the fallback " +
            "policy. Unprotected: " + string.Join(", ", open));
    }

    [Fact]
    public void The_audit_sees_the_real_route_table()
    {
        // Keeps the check above honest: an empty (or nearly empty) route list would pass it in
        // silence. Ten agent surfaces, the ledger, the model catalog, the proposals and the
        // conversation history are all mapped, so the real table is well past this floor.
        Routes().Should().HaveCountGreaterThan(15);
    }

    /// <summary>
    /// The routes this slice added, named rather than merely counted. They read and delete one
    /// person's own Roster Q&amp;A transcripts, and every one of them is scoped to the caller's own
    /// <c>UserId</c> — none takes a user id, so an unauthenticated route here would not be a
    /// leak of staff data but of whoever's history the store happened to hold.
    /// </summary>
    [Fact]
    public void The_conversation_history_routes_are_mapped_and_closed()
    {
        var history = Routes()
            .Where(r => r.Route.StartsWith("/agents/roster-qa/conversations"))
            .ToList();

        history.Select(r => $"{r.Method} {r.Route}").Should().BeEquivalentTo(
            "GET /agents/roster-qa/conversations",
            "GET /agents/roster-qa/conversations/{id:guid}",
            "DELETE /agents/roster-qa/conversations/{id:guid}",
            "DELETE /agents/roster-qa/conversations");

        history.Should().OnlyContain(r => r.Endpoint.Metadata.GetMetadata<IAuthorizeData>() != null);
        history.Should().NotContain(r => r.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null);
    }

    private sealed record MappedRoute(string Method, string Route, RouteEndpoint Endpoint);

    private static List<MappedRoute> Routes()
    {
        using var factory = new WebApplicationFactory<Program>();
        // Touching the client is what builds the host and populates the route table; resolving the
        // data source off an unstarted factory would audit an empty list.
        using var _ = factory.CreateClient();

        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => new MappedRoute(
                string.Join(
                    ",",
                    e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods
                        ?? []),
                e.RoutePattern.RawText ?? string.Empty,
                e))
            .ToList();
    }
}
