using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// What the AppHost injects into each host, asserted against the AppHost's own source (EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 4).
///
/// <para><b>Why source text rather than a built model.</b> Standing the AppHost up would start
/// Docker, a Postgres volume and Keycloak to read back four strings, and the thing at risk here is
/// not what Aspire does with a value — it is whether the line was written at all. An omitted
/// <c>WithEnvironment</c> is invisible: the host falls back to its own appsettings.json, which
/// today agrees, so nothing fails until the day somebody changes one of the two and not the other.
/// The same reading applies to the keys: the MCP host embedded on whatever
/// <c>GEMINI_API_KEY</c> the developer's shell happened to export, and an Azure key was never going
/// to arrive that way.</para>
///
/// <para>The other half of the loop is <c>Web.Tests/ProviderConfigAgreementTests</c>, which asserts
/// the three shipped settings files agree with each other. This one asserts the AppHost agrees with
/// them, so the orchestrated stack and a solo <c>dotnet run</c> cannot come to disagree.</para>
/// </summary>
public class AppHostWiringTests
{
    private const string ChatKey = "Ai:Chat:Provider";
    private const string EmbeddingsKey = "Ai:Embeddings:Provider";

    /// <summary>
    /// The MCP host is the one that embeds, so it is the one that needs a credential — and both are
    /// passed, as they are to Agents. Which one is read is decided by the discriminator, and the
    /// seam reads only the active provider's variable (EXP-53), so forwarding both cannot send one
    /// vendor's key to the other.
    /// </summary>
    [Theory]
    [InlineData("experttojob-mcp", "GEMINI_API_KEY")]
    [InlineData("experttojob-mcp", "AZURE_FOUNDRY_API_KEY")]
    [InlineData("experttojob-agents", "GEMINI_API_KEY")]
    [InlineData("experttojob-agents", "AZURE_FOUNDRY_API_KEY")]
    public void Mcp_host_receives_both_provider_keys(string resource, string variable)
        => RegistrationOf(resource).Should().Contain($"WithEnvironment(\"{variable}\"",
            $"'{resource}' cannot use a credential the AppHost never hands it");

    /// <summary>Both names, into all three hosts. The Web host runs neither backend and derives the
    /// Art. 15 recipient list from exactly these two keys, which is why it is on this list at
    /// all.</summary>
    [Theory]
    [InlineData("experttojob-web")]
    [InlineData("experttojob-mcp")]
    [InlineData("experttojob-agents")]
    public void Mcp_host_receives_both_provider_names(string resource)
        => RegistrationOf(resource).Should()
            .Contain("WithEnvironment(\"Ai__Chat__Provider\"").And
            .Contain("WithEnvironment(\"Ai__Embeddings__Provider\"");

    /// <summary>
    /// And the value it injects is the one the hosts ship with. The AppHost overriding a host's own
    /// appsettings.json is the point — it is how the orchestrated stack is kept uniform — which is
    /// exactly why a disagreement here would be invisible: every host would start, and the stack
    /// would behave unlike every other way of running it.
    /// </summary>
    [Theory]
    [InlineData("chatProvider", ChatKey, "api/Agents/appsettings.json")]
    [InlineData("embeddingsProvider", EmbeddingsKey, "api/Mcp/appsettings.json")]
    public void The_injected_provider_is_the_one_the_hosts_ship_with(
        string constant, string key, string authoritativeSettings)
    {
        var declared = Regex.Match(AppHostSource(), $@"const string {constant} = ""([^""]+)"";");
        declared.Success.Should().BeTrue($"the AppHost declares '{constant}' once, as a literal");

        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), authoritativeSettings))
            .Build()[key];

        declared.Groups[1].Value.Should().Be(shipped,
            $"the AppHost injects '{key}' over what '{authoritativeSettings}' says, so the two "
            + "disagreeing means the orchestrated stack runs a provider no other way of starting "
            + "this repo would run");
    }

    /// <summary>Keeps the reader honest: a regex that matched nothing would let every assertion
    /// above pass on an empty string.</summary>
    [Fact]
    public void The_reader_finds_every_host_it_asserts_about()
    {
        foreach (var resource in new[] { "experttojob-web", "experttojob-mcp", "experttojob-agents" })
        {
            RegistrationOf(resource).Should().Contain("WithReference(db",
                $"'{resource}' is a project resource with a database reference; a reader that "
                + "found no such text is reading the wrong thing");
        }
    }

    /// <summary>One resource's <c>AddProject</c> call, from its name to the statement's semicolon.
    /// Fluent chains, so the whole registration is one statement.</summary>
    private static string RegistrationOf(string resource)
    {
        var source = AppHostSource();
        var start = source.IndexOf($"\"{resource}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the AppHost registers a resource named '{resource}'");

        var end = source.IndexOf(';', start);
        end.Should().BeGreaterThan(start, $"'{resource}'s registration is a terminated statement");

        return source[start..end];
    }

    private static string AppHostSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "api/AppHost/Program.cs"));

    /// <summary>Walks up from the test binary until the solution file appears — the tests run from
    /// <c>bin/</c>, and hard-coding a depth breaks the first time the layout moves.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the AppHost wiring check cannot run.");
    }
}
