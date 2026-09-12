using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;
using Xunit.Abstractions;

namespace ExpertToJob.Mcp.Tests.CostFloors;

/// <summary>
/// The invariant behind the schema ceilings (P1T-223): a tool publishes its CALLER's parameters and
/// nothing else. Every tool method also takes the Application service it delegates to, which the
/// server injects and must never advertise — the model cannot supply an <c>ILanguageService</c>, and
/// a leaked one is both a nonsense argument and roughly 42 characters of schema paid on every
/// iteration of every agent shown the tool.
///
/// <para>This is what turned <c>language_update</c> from 246 into 256 estimated tokens on one CI run
/// of an unchanged tree. Microsoft.Extensions.AI.Abstractions 10.7.0 keyed a method's
/// parameter-binding options on <see cref="ParameterInfo"/> object identity, while the schema
/// generator re-fetched its own instances from <see cref="MethodBase.GetParameters"/>; a second
/// thread racing that method's non-atomic lazy parameter cache made the lookup miss and dropped
/// <c>ExcludeFromSchema</c> silently. dotnet/extensions#7677 fixed it and api/Mcp pins 10.9.0.</para>
///
/// <para>This assertion is strictly tighter than the byte ceiling and it names the fragment, so a
/// recurrence reads as "language_update published languages" rather than as a number that moved.</para>
/// </summary>
public class ToolSurfaceServiceLeakTests(ITestOutputHelper output)
{
    [Fact]
    public async Task No_tool_publishes_a_parameter_the_server_injects()
    {
        using var factory = McpTestHost.CreateFactory(nameof(No_tool_publishes_a_parameter_the_server_injects));
        await using var client = await McpTestHost.ConnectAsync(
            factory,
            McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope));

        var isService = factory.Services.GetRequiredService<IServiceProviderIsService>();
        var methods = ToolMethodsByName();

        var tools = await client.ListToolsAsync();
        tools.Should().NotBeEmpty("a surface with no tools would pass this vacuously");

        using var _ = new AssertionScope();
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            methods.Should().ContainKey(tool.Name, "every advertised tool comes from a [McpServerTool] method");
            if (!methods.TryGetValue(tool.Name, out var method)) continue;

            var schemaJson = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
            var published = PublishedProperties(schemaJson);

            foreach (var parameter in method.GetParameters())
            {
                if (parameter.Name is null ||
                    parameter.ParameterType == typeof(CancellationToken) ||
                    !isService.IsService(parameter.ParameterType))
                {
                    continue;
                }

                published.Should().NotContain(
                    parameter.Name,
                    $"{tool.Name} takes {parameter.ParameterType.Name} {parameter.Name} from the container, so the " +
                    $"model must never be shown it — schema was {schemaJson}");
            }
        }

        output.WriteLine($"{tools.Count} tools checked against the container's injectable types");
    }

    private static HashSet<string> PublishedProperties(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return doc.RootElement.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    private static Dictionary<string, MethodInfo> ToolMethodsByName() =>
        typeof(ExpertToJob.Mcp.Tools.LanguageTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToDictionary(x => x.Attribute!.Name ?? x.Method.Name, x => x.Method, StringComparer.Ordinal);
}
