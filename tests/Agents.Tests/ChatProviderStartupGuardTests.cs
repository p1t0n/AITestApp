using ExpertToJob.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The Production credential guard (EXP-18, <c>manuals/adr-chat-provider-seam.md</c> §2 decision 6):
/// a Production host refuses to boot without a key for <b>the provider it is actually configured
/// for</b>. Before this, the check was unconditionally about Gemini, so an Azure-configured
/// Production host with no Azure key started happily and failed on the first agent call — the one
/// failure this guard exists to move from runtime to startup.
///
/// <para>Two failure modes are kept apart deliberately, and both halves are pinned here. A typo'd
/// <c>Ai:Chat:Provider</c> is wrong in <em>every</em> environment and throws out of the seam
/// (<c>ChatProviderRegistrationTests.UnknownProvider_ThrowsAtStartup</c>). A <em>missing
/// credential</em> is a normal dev condition, where the agents degrade rather than crash, so it
/// throws in Production only.</para>
///
/// <para>The environment variable is read through an injected reader rather than off the process,
/// so "without a key" means the same thing here as on CI — where <c>GEMINI_API_KEY</c> may well be
/// exported for the live suites — and no test mutates process-wide state that another test running
/// beside it is reading.</para>
/// </summary>
public class ChatProviderStartupGuardTests
{
    private const string ProviderKey = "Ai:Chat:Provider";
    private const string GeminiKeyPath = "Ai:Gemini:ApiKey";
    private const string AzureKeyPath = "Ai:AzureFoundry:ApiKey";

    /// <summary>No key anywhere: every environment variable this guard could ask for is absent.
    /// </summary>
    private static readonly Func<string, string?> NoEnvironmentVariables = _ => null;

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    /// <summary>
    /// The regression this ticket exists to prevent, stated per provider: in Production, with no
    /// credential in either place the active branch reads, the host stops — and the message names
    /// the variable <em>that</em> provider wants, so the fix is one export away rather than a guess.
    /// </summary>
    [Theory]
    [InlineData("Gemini", "GEMINI_API_KEY", "AZURE_FOUNDRY_API_KEY")]
    [InlineData("AzureFoundry", "AZURE_FOUNDRY_API_KEY", "GEMINI_API_KEY")]
    public void Production_WithoutActiveProviderKey_Throws(
        string provider, string expectedVariable, string otherProvidersVariable)
    {
        var act = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, provider)),
            Environment("Production"),
            NoEnvironmentVariables);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectedVariable}*",
                "the throw has to name the variable the active provider reads")
            .Which.Message.Should().NotContain(otherProvidersVariable,
                "naming the idle provider's variable sends the operator to set the wrong key");
    }

    /// <summary>A credential in either place the construction branch looks — the environment
    /// variable it reads by name, or the provider's own <c>ApiKey</c> config path — satisfies the
    /// guard. Both sources matter: the env var is how a deployment supplies it, the config path is
    /// how user-secrets and the AppHost parameter do.</summary>
    [Theory]
    [InlineData("Gemini", "GEMINI_API_KEY", GeminiKeyPath)]
    [InlineData("AzureFoundry", "AZURE_FOUNDRY_API_KEY", AzureKeyPath)]
    public void Production_WithActiveProviderKey_Boots(
        string provider, string variable, string configPath)
    {
        var fromEnvironment = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, provider)),
            Environment("Production"),
            name => name == variable ? "a-real-key" : null);

        var fromConfiguration = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, provider), (configPath, "a-real-key")),
            Environment("Production"),
            NoEnvironmentVariables);

        fromEnvironment.Should().NotThrow();
        fromConfiguration.Should().NotThrow();
    }

    /// <summary>
    /// The Production-only half of the rule, pinned so a later edit cannot quietly tighten it:
    /// running the agents with no key at all is a normal development condition. The agents degrade
    /// on a failed model call; a host that refused to start would make every non-agent feature
    /// unreachable for want of a key the developer never needed.
    /// </summary>
    [Theory]
    [InlineData("Gemini")]
    [InlineData("AzureFoundry")]
    public void Development_WithoutKey_Boots(string provider)
    {
        var act = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, provider)),
            Environment("Development"),
            NoEnvironmentVariables);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The regression this ticket exists to prevent, from the other side: an Azure-configured
    /// Production host holding a perfectly good Azure key must not be stopped for want of a Gemini
    /// <em>chat</em> key it will never use. Embeddings are a separate requirement that still calls
    /// Google whatever chat does — they are registered in the MCP host, not this one, and nothing
    /// here touches them.
    /// </summary>
    [Fact]
    public void Production_AzureProvider_DoesNotRequireGeminiChatKey()
    {
        var act = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, "AzureFoundry"), (AzureKeyPath, "a-real-key")),
            Environment("Production"),
            NoEnvironmentVariables);

        act.Should().NotThrow(
            "the Gemini key is absent from configuration and from the environment, and on this "
            + "provider nothing asks chat for one");
    }

    /// <summary>An unknown provider name still fails, in Production as everywhere else — the guard
    /// reads the discriminator through the seam's own reader rather than parsing it a second way,
    /// so the two cannot drift into disagreeing about what a provider name is.</summary>
    [Fact]
    public void Production_UnknownProvider_Throws()
    {
        var act = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, "Vertex"), (GeminiKeyPath, "a-real-key")),
            Environment("Production"),
            NoEnvironmentVariables);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Vertex*");
    }

    private static IHostEnvironment Environment(string environmentName) =>
        new StubEnvironment { EnvironmentName = environmentName };

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ExpertToJob.Agents.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
