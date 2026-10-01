using System.Collections.Generic;
using System.Linq;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Search;
using ExpertToJob.Mcp.Auth;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The literal <c>tools/call</c> result an agent reads for every failure class, frozen (EXP-86).
///
/// <para>These assert the exact JSON an agent self-corrects against — the <c>isError</c> flag and
/// the content text, character for character — rather than "contains not_found". That is the point:
/// the try/catch that produces them moved from a wrapper around all 37 tool bodies
/// (<c>McpToolExecutor.RunAsync</c>) to one call-tool filter, and a refactor of the machinery is
/// only safe if the wire shape it produces is pinned first. Each error class is raised from the
/// same stubbed service so the five results are directly comparable.</para>
/// </summary>
public class McpToolErrorResultFreezeTests
{
    /// <summary>The one text block an MCP tool result carries — asserted literally, not searched.</summary>
    private static string OnlyText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private static WebAppFactory Host(string dbName, Exception thrown) =>
        new(dbName, thrown);

    private sealed class WebAppFactory : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _inner;

        public WebAppFactory(string dbName, Exception thrown)
        {
            _inner = McpTestHost.CreateFactory(dbName).WithWebHostBuilder(b =>
                b.ConfigureServices(services =>
                {
                    services.RemoveAll<ISemanticSearchService>();
                    services.AddScoped<ISemanticSearchService>(_ => new ThrowingSearch(thrown));
                }));
        }

        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory => _inner;

        public void Dispose() => _inner.Dispose();
    }

    private sealed class ThrowingSearch(Exception thrown) : ISemanticSearchService
    {
        public Task<SemanticSearchResult> SearchAsync(
            string query, SemanticSearchFilters? filters, int? topK, CancellationToken ct) =>
            throw thrown;
    }

    private static async Task<CallToolResult> Search(WebAppFactory host)
    {
        await using var client = await McpTestHost.ConnectAsync(host.Factory);
        return await client.CallToolAsync(
            "roster_semantic_search", new Dictionary<string, object?> { ["query"] = "payments" });
    }

    [Fact]
    public async Task NotFound_is_a_not_found_error_result()
    {
        using var host = Host(nameof(NotFound_is_a_not_found_error_result), new NotFoundException("Expert", "a1b2"));

        var result = await Search(host);

        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be("""{"code":"not_found","message":"Expert \u0027a1b2\u0027 was not found.","fields":[]}""");
    }

    [Fact]
    public async Task Conflict_is_a_conflict_error_result()
    {
        using var host = Host(nameof(Conflict_is_a_conflict_error_result), new ConflictException("Expert already has this skill."));

        var result = await Search(host);

        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be("""{"code":"conflict","message":"Expert already has this skill.","fields":[]}""");
    }

    [Fact]
    public async Task Validation_is_a_validation_error_result_carrying_every_field()
    {
        using var host = Host(
            nameof(Validation_is_a_validation_error_result_carrying_every_field),
            new ValidationException(
            [
                new ValidationFailure("FirstName", "FirstName must not be empty."),
                new ValidationFailure("Email", "Email is invalid."),
            ]));

        var result = await Search(host);

        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be(
            """{"code":"validation","message":"One or more fields are invalid.","fields":"""
            + """[{"field":"FirstName","message":"FirstName must not be empty."},"""
            + """{"field":"Email","message":"Email is invalid."}]}""");
    }

    [Fact]
    public async Task An_unmapped_exception_is_not_dressed_up_as_a_domain_error()
    {
        using var host = Host(
            nameof(An_unmapped_exception_is_not_dressed_up_as_a_domain_error),
            new InvalidOperationException("boom"));

        var result = await Search(host);

        // Not a domain failure, so the mapper does not claim it and the SDK's own generic
        // tool-invocation catch produces the result: no machine code, no per-field detail,
        // nothing an agent would try to self-correct against.
        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be("""An error occurred invoking 'roster_semantic_search'.""");
    }

    [Fact]
    public async Task A_grant_refusal_is_a_forbidden_error_result_untouched_by_the_error_path()
    {
        // The other filter's denial. It is returned, not thrown, so it must reach the agent
        // verbatim whichever order the two call-tool filters compose in.
        using var factory = McpTestHost.CreateFactory(
            nameof(A_grant_refusal_is_a_forbidden_error_result_untouched_by_the_error_path));
        await using var client = await McpTestHost.ConnectAsync(
            factory,
            McpTestHost.MintToken(
                McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope,
                McpScopes.ForTool("cv_get")));

        var result = await client.CallToolAsync(
            "expert_delete", new Dictionary<string, object?> { ["id"] = Guid.NewGuid().ToString() });

        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be(
            """{"code":"forbidden","message":"\u0027expert_delete\u0027 is not among the tools """
            + """this token grants. It is not part of this agent\u0027s tool surface \u2014 use """
            + """one of the tools you were listed.","fields":[]}""");
    }

    [Fact]
    public async Task A_void_tool_returns_the_ok_payload_on_success()
    {
        using var factory = McpTestHost.CreateFactory(nameof(A_void_tool_returns_the_ok_payload_on_success));
        await using var client = await McpTestHost.ConnectAsync(factory);
        var expert = McpTestHost.SeedExpert(factory);

        var result = await client.CallToolAsync(
            "expert_delete", new Dictionary<string, object?> { ["id"] = expert.Id.ToString() });

        result.IsError.Should().NotBe(true);
        OnlyText(result).Should().Be("""{"ok":true}""");
    }
}
