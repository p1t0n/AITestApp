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

    /// <summary>
    /// The guard and the runtime have to answer the same question the same way (EXP-97). They did
    /// not: <see cref="ChatCredential.Resolve"/> took the environment variable whenever it had any
    /// length at all, so a variable of only whitespace beat a perfectly good config key and resolved
    /// to <em>missing</em>, while this guard tested the same variable with
    /// <see cref="string.IsNullOrWhiteSpace"/>, fell back to config, and called it <em>present</em>.
    /// A Production host set up that way booted and then returned the EXP-93 503 on every agent
    /// call — the exact failure the guard exists to move from request time to startup.
    ///
    /// <para>The rule asserted here is <b>whitespace counts as unset</b>, on both sides: a variable
    /// exported empty is an operator who has not set it, not an operator who set it to a space. So
    /// config wins, and both halves agree the key is present.</para>
    ///
    /// <para>Stated as an equivalence rather than two separate expectations, because the defect was
    /// never in either answer alone — each was defensible — but in the two disagreeing. A case added
    /// here fails if the guard and the runtime part ways on it, whichever way they part.</para>
    /// </summary>
    [Theory]
    [InlineData(null, "", false)]
    [InlineData(null, "a-real-key", true)]
    [InlineData("a-real-key", "", true)]
    [InlineData("  ", "a-real-key", true)]
    [InlineData("  ", "", false)]
    [InlineData("  ", "   ", false)]
    [InlineData("", "a-real-key", true)]
    public void Guard_and_runtime_agree_on_whether_a_key_is_present(
        string? fromEnvironment, string fromConfiguration, bool present)
    {
        const ChatProvider provider = ChatProvider.Gemini;
        var variable = ChatProviderOptions.ApiKeyVariableFor(provider);
        Func<string, string?> read = name => name == variable ? fromEnvironment : null;

        var guard = () => ChatProviderStartupGuard.RequireActiveProviderCredential(
            Config((ProviderKey, provider.ToString()), (GeminiKeyPath, fromConfiguration)),
            Environment("Production"),
            read);

        var runtime = ChatCredential.Resolve(
            provider, new ChatProviderOptions { ApiKey = fromConfiguration }, read);

        runtime.IsMissing.Should().Be(!present,
            "whitespace counts as unset on both sides, so a real config key is the credential");

        if (present)
        {
            guard.Should().NotThrow("the runtime has a key, so Production must be allowed to boot");
        }
        else
        {
            guard.Should().Throw<InvalidOperationException>(
                "the runtime has no key, so the host must stop at startup rather than 503 later");
        }
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
