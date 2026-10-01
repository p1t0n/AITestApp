using System.Reflection;
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
/// The embeddings provider seam (EXP-64 and EXP-67,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decisions 1–3, 5–7):
/// <c>Ai:Embeddings:Provider</c> chooses who turns a career narrative into a vector, independently
/// of <c>Ai:Chat:Provider</c>. Both branches exist, and Azure is the default.
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
    [Theory]
    [InlineData(EmbeddingsProvider.Gemini, "GEMINI_API_KEY", "AZURE_FOUNDRY_API_KEY")]
    [InlineData(EmbeddingsProvider.AzureFoundry, "AZURE_FOUNDRY_API_KEY", "GEMINI_API_KEY")]
    public void Each_provider_reads_only_its_own_key(
        EmbeddingsProvider provider, string ownVariable, string otherVariable)
    {
        var consulted = new List<string>();
        string? Reader(string name)
        {
            consulted.Add(name);
            // Only the *other* provider's variable is exported. A branch that consults it lands on
            // a credential aimed at the wrong vendor.
            return name == otherVariable ? "the-other-vendors-key" : null;
        }

        var cfg = EmbeddingOptions.Defaults(provider);
        cfg.ApiKey = "own-key-from-config";

        var resolved = EmbeddingServiceCollectionExtensions.ResolveApiKey(provider, cfg, Reader);

        resolved.Should().Be("own-key-from-config",
            $"with no {ownVariable} exported the {provider} branch falls back to its own config "
            + $"path, never to whatever {otherVariable} happens to hold");
        consulted.Should().Equal(ownVariable);
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
    [Theory]
    [InlineData(EmbeddingsProvider.Gemini, 0.55)]
    [InlineData(EmbeddingsProvider.AzureFoundry, 0.30)]
    public void MinSimilarity_follows_the_active_provider(EmbeddingsProvider provider, double floor)
    {
        // Literals, not a re-read of Defaults(): these two numbers are the measurement (ADR §3),
        // and a test that derived them from the code under test would pass on any pair.
        var config = Config((EmbeddingServiceCollectionExtensions.ProviderKey, provider.ToString()));

        EmbeddingServiceCollectionExtensions.ResolveProvider(config).Options.MinSimilarity
            .Should().Be(floor, $"the measured {provider} plateau (ADR §3)");

        // And it is what the three search paths actually read: they all share this one options
        // object, resolved out of the container the MCP host builds.
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSearchIndexing(Config(
                (EmbeddingServiceCollectionExtensions.ProviderKey, provider.ToString()),
                ($"{EmbeddingOptions.SectionFor(provider)}:MinSimilarity", "0.62")))
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<SemanticSearchOptions>>().Value.MinSimilarity
            .Should().Be(0.62, "the provider's block is where the floor is configured now");
    }

    /// <summary>The two floors are different numbers, which is the entire reason this setting moved
    /// out of a shared <c>SemanticSearch</c> section. Asserted separately, because a refactor that
    /// collapsed them to one value would leave the theory above green on both rows.</summary>
    [Fact]
    public void The_two_providers_floors_are_not_the_same_number()
        => EmbeddingOptions.Defaults(EmbeddingsProvider.AzureFoundry).MinSimilarity
            .Should().NotBe(EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini).MinSimilarity,
                "Gemini's 0.55 scores recall@5 0.3030 on Azure's vectors (ADR §3)");

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

    /// <summary>The Azure branch (EXP-67), binding from its own block and carrying its own
    /// deployment name — which is what <c>EmbeddingModel</c> is on this provider, not a model id.
    /// The model is what every vector's tag is half of, so an unbound block here would silently
    /// stamp the wrong identity on the whole index.</summary>
    [Fact]
    public void AzureFoundry_binds_embedding_settings_from_its_own_block()
    {
        var config = Config(
            (EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"),
            ("Ai:AzureFoundry:EmbeddingModel", "text-embedding-3-small"),
            ("Ai:AzureFoundry:ApiKey", "test-key"));

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        services.GetRequiredService<EmbeddingsProvider>().Should().Be(EmbeddingsProvider.AzureFoundry);
        var embedder = services.GetRequiredService<IEmbedder>();
        embedder.Model.Should().Be("text-embedding-3-small");
        embedder.Tag.Should().Be("AzureFoundry/text-embedding-3-small");
    }

    /// <summary>The Azure block is read, not merely present: a value set there reaches the embedder
    /// the container builds. Asserted with a deployment name nobody would choose, so a branch that
    /// fell back to the code default cannot pass by coincidence.</summary>
    [Fact]
    public void The_azure_block_overrides_its_code_defaults()
    {
        var config = Config(
            (EmbeddingServiceCollectionExtensions.ProviderKey, "AzureFoundry"),
            ("Ai:AzureFoundry:EmbeddingModel", "embeddings-eu-2"),
            ("Ai:AzureFoundry:ApiKey", "test-key"));

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        services.GetRequiredService<IEmbedder>().Tag.Should().Be("AzureFoundry/embeddings-eu-2");
    }

    /// <summary>The breaker window is the active provider's own <c>QuotaBreakerSeconds</c> (ADR §2
    /// decisions 2 and 7): a minute against Azure's per-minute token cap, half an hour against
    /// Gemini's daily request allowance. Read off the embedder the container actually built —
    /// asserting the options object alone would not show that the number reaches the breaker.</summary>
    [Theory]
    [InlineData(EmbeddingsProvider.Gemini, 1800)]
    [InlineData(EmbeddingsProvider.AzureFoundry, 60)]
    public void Quota_breaker_opens_for_the_active_providers_seconds(
        EmbeddingsProvider provider, int seconds)
    {
        var config = Config(
            (EmbeddingServiceCollectionExtensions.ProviderKey, provider.ToString()),
            ($"{EmbeddingOptions.SectionFor(provider)}:ApiKey", "test-key"));

        using var services = new ServiceCollection()
            .AddLogging()
            .AddEmbeddingProvider(config)
            .BuildServiceProvider();

        services.GetRequiredService<IEmbedder>().Should().BeOfType<OpenAICompatibleEmbedder>()
            .Which.QuotaBreakerWindow.Should().Be(TimeSpan.FromSeconds(seconds));
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

        config[EmbeddingServiceCollectionExtensions.ProviderKey].Should().Be("AzureFoundry",
            "EXP-67 made Azure the default, so on the shipped stack Google receives no narrative");
        config[EmbeddingServiceCollectionExtensions.LegacyMinSimilarityKey].Should().BeNull(
            "the global floor was removed, and a leftover one throws at startup");

        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
        provider.Should().Be(EmbeddingsProvider.AzureFoundry);
        options.MinSimilarity.Should().Be(0.30);
        options.EmbeddingModel.Should().Be("text-embedding-3-small");
        options.QuotaBreakerSeconds.Should().Be(60);
        options.Endpoint.Should().Be("https://experttojob-openai-swc.openai.azure.com/openai/v1/");
        options.ApiKey.Should().BeEmpty("the key comes from AZURE_FOUNDRY_API_KEY, never a tracked file");
    }

    /// <summary>The Gemini block stays shipped and stays bindable: it is one configuration key away
    /// from being active again (ADR §5 keeps it for development and demo), and a block that quietly
    /// stopped binding would only be discovered by someone switching back.</summary>
    [Fact]
    public void The_shipped_mcp_settings_still_bind_the_provider_that_is_not_active()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "api/Mcp/appsettings.json"))
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                EmbeddingServiceCollectionExtensions.ProviderKey, "Gemini")])
            .Build();

        var (provider, options) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
        provider.Should().Be(EmbeddingsProvider.Gemini);
        options.MinSimilarity.Should().Be(0.55);
        options.EmbeddingModel.Should().Be("gemini-embedding-001");
        options.QuotaBreakerSeconds.Should().Be(1800);
    }

    /// <summary>A bare <see cref="SemanticSearchOptions"/> — what every search unit test builds —
    /// floors at the incumbent's value, because an absent provider key means Gemini. One literal,
    /// in the provider's defaults, rather than two that can drift apart.</summary>
    [Fact]
    public void The_bare_search_options_floor_at_the_incumbents_value()
        => new SemanticSearchOptions().MinSimilarity
            .Should().Be(EmbeddingOptions.Defaults(EmbeddingsProvider.Gemini).MinSimilarity);

    /// <summary>
    /// The one embedding setting that is <b>not</b> a setting. Every other value on
    /// <see cref="EmbeddingOptions"/> is a per-provider default a configuration block may override;
    /// the output dimensionality cannot be, because the database fixes it — the
    /// <c>ExpertSearchChunk.Embedding</c> column is <c>vector(1536)</c>, and Postgres rejects a
    /// vector of any other width on INSERT. A settable property is a binding target, so
    /// <c>Ai:AzureFoundry:Dimensions</c> would have been accepted in silence and then failed one
    /// layer down, per row, at write time. Held as a constant instead: unreachable from
    /// configuration by construction rather than by nobody having tried it.
    ///
    /// <para>Asserted reflectively because the point is the <em>shape</em> of the member — a
    /// literal field, not a property — which is exactly what a plain read could not tell apart.
    /// Changing the width is a schema migration (ADR §9), and it edits this test.</para>
    /// </summary>
    [Fact]
    public void The_embedding_dimensionality_is_a_constant_no_configuration_can_reach()
    {
        const int VectorColumnWidth = 1536;

        typeof(EmbeddingOptions).GetProperty("Dimensions").Should().BeNull(
            "a settable property binds from configuration; the column width is not negotiable");

        var constant = typeof(EmbeddingOptions)
            .GetField("Dimensions", BindingFlags.Public | BindingFlags.Static);

        constant.Should().NotBeNull("EmbeddingOptions.Dimensions is the one place the width is written");
        constant!.IsLiteral.Should().BeTrue("a const is what keeps the binder away from it");
        constant.GetRawConstantValue().Should().Be(VectorColumnWidth,
            "the ExpertSearchChunk.Embedding column is vector(1536)");
    }

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
