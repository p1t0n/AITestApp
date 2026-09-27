using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using ExpertToJob.Infrastructure.Search;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The embeddings provider seam (EXP-64, <c>manuals/adr-embeddings-provider-seam.md</c> §2
/// decisions 1–3, 5): <c>Ai:Embeddings:Provider</c> chooses who turns a career narrative into a
/// vector, independently of <c>Ai:Chat:Provider</c>.
///
/// <para>Every assertion here is about the <em>edge</em> — which key is read, which block is bound,
/// what throws and when. None of it calls a provider: the failures this file exists to catch are
/// the silent ones, where a host starts happily on settings nobody set, or aims a request at one
/// vendor carrying another vendor's credential (EXP-53).</para>
/// </summary>
public class EmbeddingProviderTests
{
    /// <summary>An absent key means the incumbent, the same code default chat carries. A config
    /// that never mentions embeddings keeps behaving exactly as it did before the seam.</summary>
    [Fact]
    public void Absent_provider_key_selects_Gemini()
    {
        var config = new ConfigurationBuilder().Build();

        EmbeddingServiceCollectionExtensions.ReadProvider(config)
            .Should().Be(EmbeddingsProvider.Gemini);
    }

    /// <summary>A typo'd provider name is wrong in every environment, so it throws at the
    /// configuration edge rather than degrading somewhere further in. Unlike a missing credential,
    /// which stays a normal development condition.</summary>
    [Theory]
    [InlineData("Azure")]
    [InlineData("gemini-embedding-001")]
    [InlineData("1")]
    [InlineData("Gemini,AzureFoundry")]
    [InlineData("")]
    public void Unknown_provider_throws_at_startup_in_every_environment(string configured)
    {
        var config = Config((EmbeddingServiceCollectionExtensions.ProviderKey, configured));

        var act = () => EmbeddingServiceCollectionExtensions.ReadProvider(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{EmbeddingServiceCollectionExtensions.ProviderKey}*",
                "the throw has to name the key that is wrong");
    }

    /// <summary>
    /// The bug this seam exists to make impossible (EXP-53): today's code reads
    /// <c>GEMINI_API_KEY</c> whatever the endpoint, so a request aimed at Azure would carry the
    /// Google key. Asserted by recording which variables the branch consults, not only which value
    /// it lands on — a branch that reads the other provider's variable and happens to discard it is
    /// still one refactor away from sending it.
    /// </summary>
    [Fact]
    public void Each_provider_reads_only_its_own_key()
    {
        var consulted = new List<string>();
        string? Reader(string name)
        {
            consulted.Add(name);
            return name == "AZURE_FOUNDRY_API_KEY" ? "azure-key" : null;
        }

        var cfg = EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini);
        cfg.ApiKey = "gemini-key-from-config";

        var resolved = EmbeddingServiceCollectionExtensions.ResolveApiKey(
            EmbeddingsProvider.Gemini, cfg, Reader);

        resolved.Should().Be("gemini-key-from-config",
            "with no GEMINI_API_KEY exported the Gemini branch falls back to its own config path, "
            + "never to whatever Azure's variable happens to hold");
        consulted.Should().Equal("GEMINI_API_KEY");
    }

    /// <summary>The environment variable still wins over the config path, for the provider whose
    /// variable it is.</summary>
    [Fact]
    public void The_providers_own_environment_variable_wins_over_its_config_path()
    {
        var cfg = EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini);
        cfg.ApiKey = "from-config";

        EmbeddingServiceCollectionExtensions
            .ResolveApiKey(EmbeddingsProvider.Gemini, cfg, name => name == "GEMINI_API_KEY" ? "from-env" : null)
            .Should().Be("from-env");
    }

    /// <summary>The similarity floor is calibrated per embedding model — Gemini's 0.55 hides 70% of
    /// the correct matches on Azure vectors (ADR §3) — so it comes out of the active provider's own
    /// block, and every search path takes it from there.</summary>
    [Fact]
    public void MinSimilarity_follows_the_active_provider()
    {
        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(
            new ConfigurationBuilder().Build());

        provider.Should().Be(EmbeddingsProvider.Gemini);
        options.MinSimilarity.Should().Be(0.55, "the measured Gemini plateau (ADR §3)");

        // And it is what the three search paths actually read: they all share this one options
        // object, resolved out of the container the MCP host builds.
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSearchIndexing(Config(("Ai:Gemini:MinSimilarity", "0.62")))
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<SemanticSearchOptions>>().Value.MinSimilarity
            .Should().Be(0.62, "the provider's block is where the floor is configured now");
    }

    /// <summary>The global key is gone, and a leftover one throws rather than binding to a property
    /// nobody reads — the same rule, and the same reason, as the legacy chat section.</summary>
    [Fact]
    public void Legacy_global_MinSimilarity_throws_at_startup()
    {
        var config = Config((EmbeddingServiceCollectionExtensions.LegacyMinSimilarityKey, "0.55"));

        var act = () => new ServiceCollection().AddLogging().AddEmbeddingProvider(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ai:Gemini:MinSimilarity*",
                "the throw has to name the key that replaced it, not just refuse");
    }

    /// <summary>The rest of the <c>SemanticSearch</c> section is untouched: only the one key moved.</summary>
    [Fact]
    public void The_remaining_SemanticSearch_keys_still_bind()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSearchIndexing(Config(("SemanticSearch:DefaultTopK", "7")))
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<SemanticSearchOptions>>().Value.DefaultTopK.Should().Be(7);
    }

    /// <summary>The Gemini branch binds its settings from its own block, and the embedder it builds
    /// carries the model that block names.</summary>
    [Fact]
    public void Gemini_binds_embedding_settings_from_its_own_block()
    {
        var config = Config(
            ("Ai:Gemini:EmbeddingModel", "gemini-embedding-001"),
            ("Ai:Gemini:ApiKey", "test-key"));

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        services.GetRequiredService<EmbeddingsProvider>().Should().Be(EmbeddingsProvider.Gemini);
        services.GetRequiredService<IEmbedder>().Model.Should().Be("gemini-embedding-001");
    }

    /// <summary>Azure is a member of the enum before it is a construction branch (EXP-67). It has
    /// to fail saying so, rather than binding an empty block and embedding against nothing.</summary>
    [Fact]
    public void AzureFoundry_is_named_but_not_built_yet()
    {
        var config = Config((EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"));

        EmbeddingServiceCollectionExtensions.ReadProvider(config)
            .Should().Be(EmbeddingsProvider.AzureFoundry, "the name is valid; the branch is not there yet");

        var act = () => new ServiceCollection().AddLogging().AddEmbeddingProvider(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*EXP-67*");
    }

    /// <summary>The shipped MCP settings bind where the options class reads. Asserted against the
    /// literals in the file, never the property defaults: those agreeing is what would hide an
    /// unbound section.</summary>
    [Fact]
    public void The_shipped_mcp_settings_name_the_embeddings_provider_and_its_floor()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "api/Mcp/appsettings.json"))
            .Build();

        config[EmbeddingServiceCollectionExtensions.ProviderKey].Should().Be("Gemini");
        config[EmbeddingServiceCollectionExtensions.LegacyMinSimilarityKey].Should().BeNull(
            "the global floor was removed, and a leftover one throws at startup");

        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
        provider.Should().Be(EmbeddingsProvider.Gemini);
        options.MinSimilarity.Should().Be(0.55);
        options.EmbeddingModel.Should().Be("gemini-embedding-001");
    }

    /// <summary>A bare <see cref="SemanticSearchOptions"/> — what every search unit test builds —
    /// floors at the incumbent's value, because an absent provider key means Gemini. One literal,
    /// in the provider's defaults, rather than two that can drift apart.</summary>
    [Fact]
    public void The_bare_search_options_floor_at_the_incumbents_value()
        => new SemanticSearchOptions().MinSimilarity
            .Should().Be(EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini).MinSimilarity);

    private static IConfiguration Config(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException("Could not find ExpertToJob.slnx above the test binary.");
    }
}
