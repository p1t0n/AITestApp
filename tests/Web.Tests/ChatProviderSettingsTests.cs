using ExpertToJob.Application.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// <c>Ai:Chat:Provider</c> is read by two hosts and means two different things to them. The Agents
/// host uses it to <em>choose</em> the chat backend; the Web host uses it to <em>tell a data
/// subject which company their CV is sent to</em> (Art. 15(1)(c), via
/// <see cref="Art15Disclosure.RecipientsFor"/>). Only one of those is a statement to a person, and
/// it is the one on the host that does not run chat.
///
/// <para>That asymmetry is what made EXP-61: EXP-41 switched the Agents host to
/// <c>AzureFoundry</c> and left the Web host on <c>Gemini</c>, so the privacy page named Google as
/// the AI model provider while every score was written by Azure OpenAI. Nothing failed — a wrong
/// disclosure is not a crash — and the page went on reading plausibly.</para>
///
/// <para><b>Why a test rather than one AppHost injection.</b> The AppHost could set the value once
/// for both hosts, but it only governs the orchestrated stack. <c>dotnet run --project api/Web</c>
/// is documented and supported (see <c>CLAUDE.md</c>), and a deployment that runs the two hosts
/// from their own settings files never sees the AppHost at all. The shipped files are the thing
/// that has to agree, so the shipped files are what this asserts.</para>
/// </summary>
public class ChatProviderSettingsTests
{
    private const string ProviderKey = ChatProviderDisclosure.ConfigurationKey;
    private const string EmbeddingsKey = EmbeddingsProviderDisclosure.ConfigurationKey;

    /// <summary>
    /// The host that answers <c>GET /api/me/access</c> has to name the backend that actually runs
    /// chat. Asserted as a literal rather than against the Agents file, so that a future switch
    /// that moved <em>both</em> hosts to some third provider still has to come past a human.
    /// </summary>
    [Fact]
    public void The_web_host_ships_the_provider_that_actually_runs_chat()
    {
        Shipped("api/Web/appsettings.json")[ProviderKey].Should().Be(
            "AzureFoundry",
            "the Web host derives the Art. 15 recipient list from this key, and chat runs on Azure OpenAI (EXP-41)");
    }

    /// <summary>
    /// The drift guard. Every shipped settings file that spells the key has to spell the same
    /// value: the host that chooses the provider and the host that discloses it disagreeing is
    /// exactly the bug, and it is silent by construction.
    /// </summary>
    [Fact]
    public void Every_host_that_names_the_chat_provider_names_the_same_one()
    {
        var byFile = HostSettingsFiles()
            .Select(file => (File: file, Provider: Shipped(file)[ProviderKey]))
            .Where(x => x.Provider is not null)
            .ToList();

        byFile.Should().HaveCountGreaterThanOrEqualTo(2,
            "the Web and Agents hosts both spell this key — a sweep that found fewer has stopped "
            + "looking where the drift happens, and would pass in the same silence as the bug");

        byFile.Select(x => x.Provider).Distinct(StringComparer.Ordinal).Should().ContainSingle(
            "the hosts disagreeing means the privacy page names a provider that does not run chat. "
            + "Found: " + string.Join(", ", byFile.Select(x => $"{x.File}={x.Provider}")));
    }

    /// <summary>Keeps the sweep honest: it has to be reading the two files it claims to.</summary>
    [Fact]
    public void The_sweep_reads_the_settings_files_it_claims_to()
    {
        HostSettingsFiles().Should()
            .Contain("api/Web/appsettings.json").And
            .Contain("api/Agents/appsettings.json");
    }

    /// <summary>
    /// The end the data subject reads, from the shipped file alone: no host, no database, no
    /// container. It is the same two steps <c>api/Web/Program.cs</c> takes at startup, so a value
    /// that parses to nothing shows up here as a named failure rather than as a page that quietly
    /// over-discloses.
    /// </summary>
    [Fact]
    public void The_shipped_web_settings_disclose_microsoft_as_the_model_provider()
    {
        var web = Shipped("api/Web/appsettings.json");
        var chat = ChatProviderDisclosure.From(web[ProviderKey]);
        var embeddings = EmbeddingsProviderDisclosure.From(web[EmbeddingsKey]);

        chat.Provider.Should().Be(DisclosedChatProvider.AzureFoundry);

        var recipients = Art15Disclosure.RecipientsFor(chat.Provider, embeddings.Provider);
        recipients.Single(r => r.Recipient.Contains("AI model provider"))
            .Recipient.Should().Contain("Microsoft");
        recipients.Single(r => r.Recipient.Contains("embeddings provider"))
            .Recipient.Should().Contain("Google", "embeddings are still on Gemini until EXP-67");
    }

    /// <summary>
    /// The same drift, on the second key (EXP-66). The Web host does not embed either, so a value
    /// here that disagrees with the host that <em>does</em> is the exact EXP-61 failure one key
    /// over: nothing breaks, and the privacy page names a company that receives nothing.
    ///
    /// <para>Scoped to the two files that spell the key today. EXP-67 widens this to a sweep over
    /// all three hosts and both keys once the Agents host names one too.</para>
    /// </summary>
    [Fact]
    public void The_web_host_discloses_the_embeddings_provider_the_mcp_host_actually_runs()
    {
        var disclosed = Shipped("api/Web/appsettings.json")[EmbeddingsKey];
        var running = Shipped("api/Mcp/appsettings.json")[EmbeddingsKey];

        running.Should().NotBeNull("the MCP host is the one that embeds, and it names its provider");
        disclosed.Should().Be(running,
            "the Web host derives the Art. 15 embeddings recipient from this key, and a host that "
            + "does not embed cannot notice it is wrong");

        EmbeddingsProviderDisclosure.From(disclosed).Provider.Should().NotBeNull(
            "a value that parses to nothing makes the page over-disclose rather than fail");
    }

    private static IConfigurationRoot Shipped(string relativePath) =>
        new ConfigurationBuilder().AddJsonFile(Path.Combine(RepoRoot(), relativePath)).Build();

    /// <summary>
    /// Every host's base settings file, discovered rather than listed, so a host added later is
    /// swept the day it ships instead of the day somebody remembers this file. Environment
    /// overlays (<c>appsettings.Development.json</c>) are deliberately out: they are allowed to
    /// differ per environment, and none of them names this key today.
    /// </summary>
    private static List<string> HostSettingsFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "api"), "appsettings.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>Walks up from the test binary until the solution file appears — the tests run out
    /// of <c>bin/</c>, and a hard-coded depth breaks the first time the layout moves.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the settings sweep cannot run.");
    }
}
