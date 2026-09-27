using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The Production credential guard for embeddings (EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 6) and the development behaviour it
/// deliberately leaves alone.
///
/// <para>The two halves are one decision, so they are asserted together. A Production MCP host with
/// no key for its active provider must not boot: its search would go on answering, from the lexical
/// fallback, and nobody would be told the roster stopped being searched semantically. The same host
/// in Development must boot, because running the stack without a key is how most of this repo gets
/// worked on — and there the honest behaviour is keyword matching, not an SDK exception on every
/// query.</para>
/// </summary>
public class EmbeddingProviderStartupGuardTests
{
    [Theory]
    [InlineData(EmbeddingsProvider.AzureFoundry, "AZURE_FOUNDRY_API_KEY")]
    [InlineData(EmbeddingsProvider.Gemini, "GEMINI_API_KEY")]
    public void Production_refuses_to_boot_without_a_key_for_the_active_provider(
        EmbeddingsProvider provider, string variable)
    {
        var act = () => EmbeddingProviderStartupGuard.RequireActiveProviderCredential(
            Config((EmbeddingServiceCollectionExtensions.ProviderKey, provider.ToString())),
            Environment(Environments.Production),
            NoVariablesSet);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{variable}*",
                "the throw has to name the variable to set, not only refuse to start");
    }

    /// <summary>The guard asks for the <em>active</em> provider's key, and the other vendor's does
    /// not satisfy it. Without this, an Azure-configured Production host would boot on a leftover
    /// GEMINI_API_KEY and fail on the first embed — a startup mistake found at request time, which
    /// is the failure EXP-18 fixed one seam over.</summary>
    [Fact]
    public void The_other_providers_key_does_not_satisfy_the_guard()
    {
        var act = () => EmbeddingProviderStartupGuard.RequireActiveProviderCredential(
            Config((EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry")),
            Environment(Environments.Production),
            name => name == "GEMINI_API_KEY" ? "google-key" : null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AZURE_FOUNDRY_API_KEY*");
    }

    [Theory]
    [InlineData("AZURE_FOUNDRY_API_KEY", null)]
    [InlineData(null, "Ai:AzureFoundry:ApiKey")]
    public void Production_boots_with_a_key_in_either_place_the_branch_reads(
        string? variable, string? configPath)
    {
        var entries = new List<(string, string?)>
        {
            (EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"),
        };
        if (configPath is not null)
        {
            entries.Add((configPath, "from-config"));
        }

        var act = () => EmbeddingProviderStartupGuard.RequireActiveProviderCredential(
            Config([.. entries]),
            Environment(Environments.Production),
            name => name == variable ? "from-env" : null);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Development is the other half of the same decision, and it is asserted end to end rather
    /// than as "the guard returned": the guard not throwing would be satisfied by a host that then
    /// failed on every search. What has to be true is that the container builds, an
    /// <see cref="IEmbedder"/> resolves, and asking it to embed raises the condition the query paths
    /// degrade on — <see cref="EmbeddingUnavailableException"/>, which
    /// <c>SemanticSearchService</c> answers with lexical ranking over the same chunk pool.
    /// </summary>
    [Fact]
    public async Task Development_boots_without_a_key_and_search_falls_back_to_keywords()
    {
        var config = Config((EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"));

        var boot = () => EmbeddingProviderStartupGuard.RequireActiveProviderCredential(
            config, Environment(Environments.Development), NoVariablesSet);
        boot.Should().NotThrow("running the stack without a key is a normal development condition");

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        var embedder = services.GetRequiredService<IEmbedder>();

        // It still reports the deployment's own identity: coverage, the reconciler's stale-tag test
        // and the search paths' filter all read this, and a keyless host has not changed which
        // model its deployment is configured for.
        embedder.Tag.Should().Be("AzureFoundry/text-embedding-3-small");

        (await embedder.Invoking(e => e.EmbedAsync(["anything"])).Should()
            .ThrowAsync<EmbeddingUnavailableException>(
                "this is the exception SemanticSearchService answers with the lexical fallback"))
            .WithMessage("*AZURE_FOUNDRY_API_KEY*", "and it has to say which key is missing");
    }

    /// <summary>Nothing is sent anywhere: the stand-in holds no client at all, so an empty batch is
    /// answered without a provider ever being contacted.</summary>
    [Fact]
    public async Task The_keyless_stand_in_sends_nothing()
    {
        var embedder = new CredentiallessEmbedder(EmbeddingsProvider.Gemini, "gemini-embedding-001");

        var batch = await embedder.EmbedAsync([]);

        batch.Vectors.Should().BeEmpty();
        batch.InputTokens.Should().Be(0);
        embedder.Model.Should().Be("gemini-embedding-001");
        embedder.Tag.Should().Be("Gemini/gemini-embedding-001");
    }

    /// <summary>A key present means the real embedder, not the stand-in. The registration is a
    /// branch, and a branch that took the wrong side would make a fully configured host silently
    /// stop embedding.</summary>
    [Fact]
    public void A_configured_key_builds_the_real_embedder()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(Config(
                (EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"),
                ("Ai:AzureFoundry:ApiKey", "test-key")))
            .BuildServiceProvider();

        services.GetRequiredService<IEmbedder>().Should().BeOfType<OpenAICompatibleEmbedder>();
    }

    private static string? NoVariablesSet(string name) => null;

    private static IConfiguration Config(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static IHostEnvironment Environment(string name) =>
        new HostingEnvironment { EnvironmentName = name };

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
