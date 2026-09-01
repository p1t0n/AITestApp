using System.Security.Claims;
using ExpertToJob.Mcp.Auth;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Tool Grants (P1T-149): the per-tool half of MCP authorization, carried by the token as
/// <c>mcp:tool:&lt;name&gt;</c> scopes and enforced server-side at <c>tools/list</c> and
/// <c>tools/call</c>.
///
/// <para>This is P1T-146's client-side Tool Allowlist moved onto the identity. The behaviour the
/// allowlist already had is preserved deliberately — narrowing is opt-in, an absent grant set
/// means "everything the capability scopes carry" — and the two things it could not do are what
/// the rest of this asserts: a token is now genuinely unable to call what it was not shown, and a
/// grant is not a second route to capability.</para>
/// </summary>
public class ToolGrantTests
{
    private static ClaimsPrincipal Principal(params string[] scopes) =>
        new(new ClaimsIdentity([new Claim("scope", string.Join(' ', scopes))], "test"));

    // ---- the rule ----

    [Fact]
    public void A_token_with_no_grants_narrows_nothing()
    {
        // The same rule as an absent Tool Allowlist, and for the same reason: a forgotten
        // client-scope assignment must not quietly cripple an agent. expert-to-job-mcp — the
        // interactive human client — is the identity that legitimately lives here.
        var grants = McpToolGrants.Of(Principal(McpScopes.Read, McpScopes.Write));

        grants.ShowsEverything.Should().BeTrue();
        grants.Allows("expert_delete").Should().BeTrue();
    }

    [Fact]
    public void A_token_with_grants_is_narrowed_to_exactly_them()
    {
        var grants = McpToolGrants.Of(Principal(
            McpScopes.Read, McpScopes.ForTool("cv_get"), McpScopes.ForTool("skill_list")));

        grants.ShowsEverything.Should().BeFalse();
        grants.ToolNames.Should().BeEquivalentTo(["cv_get", "skill_list"]);
        grants.Allows("cv_get").Should().BeTrue();
        grants.Allows("expert_list").Should().BeFalse();
    }

    [Fact]
    public void Scopes_split_across_claims_are_read_as_one_set()
    {
        // An OAuth "scope" claim may arrive once space-delimited or split; both are the same token.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scope", McpScopes.Read), new Claim("scope", McpScopes.ForTool("cv_get"))],
            "test"));

        McpToolGrants.Of(user).ToolNames.Should().BeEquivalentTo(["cv_get"]);
    }

    [Fact]
    public void An_unauthenticated_principal_narrows_nothing()
    {
        // Grants only ever narrow, so "no principal" cannot mean "no tools" — the capability
        // scopes are what refuse an anonymous caller, and they already do.
        McpToolGrants.Of(null).ShowsEverything.Should().BeTrue();
        McpToolGrants.Of(new ClaimsPrincipal(new ClaimsIdentity())).ShowsEverything.Should().BeTrue();
    }

    // ---- enforced against the real server ----

    [Fact]
    public async Task Tools_list_advertises_only_the_granted_tools()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Tools_list_advertises_only_the_granted_tools));
        var token = McpTestHost.MintToken(
            McpScopes.Read, McpScopes.ForTool("cv_get"), McpScopes.ForTool("expert_list"));
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var names = (await client.ListToolsAsync()).Select(t => t.Name);

        names.Should().BeEquivalentTo(["cv_get", "expert_list"]);
    }

    [Fact]
    public async Task Calling_an_ungranted_tool_is_refused()
    {
        // The half the client-side allowlist structurally could not do. Before this, a filtered
        // client could still call what it had discarded, because its token was entitled to the
        // whole read surface — the allowlist was a convention, not a boundary.
        using var factory = McpTestHost.CreateFactory(nameof(Calling_an_ungranted_tool_is_refused));
        McpTestHost.SeedExpert(factory);
        var token = McpTestHost.MintToken(McpScopes.Read, McpScopes.ForTool("cv_get"));
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var result = await client.CallToolAsync("expert_list");

        // Refused as a structured tool error, not a protocol fault: the agent reads the code and
        // picks another tool, the same way it self-corrects a validation or not_found failure.
        result.IsError.Should().BeTrue();
        McpTestHost.Text(result).Should()
            .Contain(ToolGrantFilters.ForbiddenCode).And.Contain("expert_list");
    }

    [Fact]
    public async Task A_granted_tool_still_works()
    {
        using var factory = McpTestHost.CreateFactory(nameof(A_granted_tool_still_works));
        McpTestHost.SeedExpert(factory);
        var token = McpTestHost.MintToken(McpScopes.Read, McpScopes.ForTool("expert_list"));
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var result = await client.CallToolAsync("expert_list");

        McpTestHost.Text(result).Should().Contain("Lovelace");
    }

    [Fact]
    public async Task A_grant_cannot_widen_what_the_capability_scope_carries()
    {
        // The two axes compose; they do not substitute. mcp:tool:expert_delete on a read-only
        // token buys nothing, because deletes need mcp:admin — which is the whole point of
        // keeping the grant prefix separate from the capability scopes.
        using var factory = McpTestHost.CreateFactory(nameof(A_grant_cannot_widen_what_the_capability_scope_carries));
        var expert = McpTestHost.SeedExpert(factory);
        var token = McpTestHost.MintToken(McpScopes.Read, McpScopes.ForTool("expert_delete"));
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        (await client.ListToolsAsync()).Should().BeEmpty();

        var call = async () => await client.CallToolAsync(
            "expert_delete", new Dictionary<string, object?> { ["id"] = expert.Id });

        // Refused by the capability policy, which is the SDK's own filter and refuses harder —
        // a protocol error, not a result. The grant never got a say, and that is the assertion.
        (await call.Should().ThrowAsync<Exception>()).Which.Message
            .Should().Contain("This tool requires authorization");
    }

    [Fact]
    public async Task An_ungranted_token_is_still_shown_its_whole_capability_surface()
    {
        // The regression guard for every identity that has no grants: adding this feature must
        // not have narrowed expert-to-job-mcp, the e2e client, or anything else by omission.
        using var factory = McpTestHost.CreateFactory(nameof(An_ungranted_token_is_still_shown_its_whole_capability_surface));
        await using var client = await McpTestHost.ConnectAsync(factory, McpTestHost.MintToken(McpScopes.Read));

        var names = (await client.ListToolsAsync()).Select(t => t.Name);

        names.Should().BeEquivalentTo(ExpertToJob.CostFloors.CostFloors.ReadScopeTools);
    }
}
