using ExpertToJob.Application.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The two provider keys are each read by more than one host, and mean different things to each.
/// The Agents host uses <c>Ai:Chat:Provider</c> to <em>choose</em> the chat backend and the MCP host
/// uses <c>Ai:Embeddings:Provider</c> to choose the embedder; the Web host uses <b>both</b> to
/// <em>tell a data subject which companies their CV is sent to</em> (Art. 15(1)(c), via
/// <see cref="Art15Disclosure.RecipientsFor"/>). Only one of those is a statement to a person, and
/// it is on the host that runs neither.
///
/// <para>That asymmetry is what made EXP-61: EXP-41 switched the Agents host to
/// <c>AzureFoundry</c> and left the Web host on <c>Gemini</c>, so the privacy page named Google as
/// the AI model provider while every score was written by Azure OpenAI. Nothing failed — a wrong
/// disclosure is not a crash — and the page went on reading plausibly.</para>
///
/// <para><b>Why a test and not only the AppHost injection.</b> Since EXP-67 the AppHost does set
/// both values once and inject them into all three hosts — but it only governs the orchestrated
/// stack. <c>dotnet run --project api/Web</c> is documented and supported (see <c>CLAUDE.md</c>),
/// and a deployment that runs the three hosts from their own settings files never sees the AppHost
/// at all. The shipped files are the thing that has to agree, so the shipped files are what this
/// asserts; <c>ServiceDefaults.Tests/AppHostWiringTests</c> then holds the AppHost to them.</para>
/// </summary>
public class ProviderConfigAgreementTests
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
    /// The drift guard, over both keys (EXP-67, ADR §2 decision 4). Every shipped settings file
    /// that spells a provider key has to spell the same value for it: the host that chooses a
    /// provider and the host that discloses it disagreeing is exactly the bug, and it is silent by
    /// construction.
    ///
    /// <para>Asserted on the shipped files rather than on the AppHost's injection, because the
    /// AppHost only governs the orchestrated stack. <c>dotnet run --project api/Web</c> is
    /// documented and supported, and a deployment that runs three hosts from their own settings
    /// files never sees the AppHost at all. <c>AppHostWiringTests</c> pins the other direction —
    /// that what the AppHost injects equals what these files say.</para>
    /// </summary>
    [Theory]
    // Chat is chosen in Agents and disclosed by Web. The MCP host does not chat, so it does not
    // name a chat provider: a key nothing reads is a value nobody maintains.
    [InlineData(ProviderKey, "api/Agents/appsettings.json", "api/Web/appsettings.json")]
    // Embeddings are chosen in MCP and disclosed by Web — and named by Agents too since EXP-67, so
    // the AppHost has one value to inject into all three and no host can be started on a stale one.
    [InlineData(EmbeddingsKey,
        "api/Agents/appsettings.json", "api/Mcp/appsettings.json", "api/Web/appsettings.json")]
    public void Web_Mcp_and_Agents_appsettings_agree_on_both_providers(
        string key, params string[] expectedToNameIt)
    {
        var byFile = HostSettingsFiles()
            .Select(file => (File: file, Provider: Shipped(file)[key]))
            .Where(x => x.Provider is not null)
            .ToList();

        // The exact set, not a floor. A host that stopped naming the key would fall back to the
        // seam's code default, which is the incumbent — so the drift would be a host quietly
        // running the provider this repo moved away from, and a "found at least two" sweep would
        // not notice.
        byFile.Select(x => x.File).Should().BeEquivalentTo(expectedToNameIt,
            $"exactly these hosts spell '{key}'; one that stopped, or a new one that started, "
            + "changes who is bound by the agreement below");

        byFile.Select(x => x.Provider).Distinct(StringComparer.Ordinal).Should().ContainSingle(
            $"the hosts disagreeing on '{key}' means the privacy page names a provider that "
            + "receives nothing. Found: "
            + string.Join(", ", byFile.Select(x => $"{x.File}={x.Provider}")));
    }

    /// <summary>Keeps the sweep honest: it has to be reading the three files it claims to.</summary>
    [Fact]
    public void The_sweep_reads_the_settings_files_it_claims_to()
    {
        HostSettingsFiles().Should()
            .Contain("api/Web/appsettings.json").And
            .Contain("api/Agents/appsettings.json").And
            .Contain("api/Mcp/appsettings.json");
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
        embeddings.Provider.Should().Be(DisclosedEmbeddingsProvider.AzureFoundry);

        var recipients = Art15Disclosure.RecipientsFor(chat.Provider, embeddings.Provider);

        // One provider doing both jobs is one entry, not two. The absence is the assertion that
        // matters: Google is not named anywhere in the present tense, because on the shipped stack
        // it receives nothing (EXP-67). A former-recipient entry is a separate, past-tense thing,
        // and it is not built from configuration at all.
        recipients.Single(r => r.Recipient.Contains("AI model provider"))
            .Recipient.Should().Contain("Microsoft");
        recipients.Should().NotContain(r => r.Recipient.Contains("as our embeddings provider"));
        recipients.Should().NotContain(r => r.Recipient.Contains("Google"));

        Art15Disclosure.SearchIndexNoteFor(embeddings.Provider)
            .Should().Contain("Microsoft").And.Contain("within the EU");
    }

    /// <summary>
    /// The same drift, on the second key (EXP-66), named as the pair it is about: the Web host does
    /// not embed, so a value here that disagrees with the host that <em>does</em> is the exact
    /// EXP-61 failure one key over — nothing breaks, and the privacy page names a company that
    /// receives nothing.
    ///
    /// <para>Kept alongside the sweep above rather than folded into it. The sweep proves the files
    /// agree with each other; this names <em>which</em> host is the source of truth for this key,
    /// which is the thing a reader has to know to fix a disagreement in the right direction.</para>
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
