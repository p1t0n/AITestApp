using System.Text.RegularExpressions;
using ExpertToJob.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The safety net for the <c>Ai:*</c> configuration shape (EXP-15, ADR §2 decision 3). A renamed
/// configuration key is not a compile error: a stale <c>Gemini</c>-prefixed path binds to nothing,
/// the options object falls back to its property defaults, and the host starts looking healthy
/// while reading none of the values anyone set. That is the failure this file exists to make loud.
///
/// <para>Two halves, because a stale key can arrive from two directions. The sweep reads the
/// repo's own source and fails on a legacy key someone re-typed — the pattern
/// <c>web/src/frozenHooks.test.ts</c> already uses for <c>data-testid</c> names, and
/// <c>Application.Tests/VisibilitySeamTests</c> uses for the visibility predicate. The startup
/// throw catches the half the sweep cannot see: a legacy section arriving from an environment
/// variable, a user-secret or a deployment's own config, none of which live in this tree.</para>
///
/// <para>The needle is spelled as a concatenation on purpose, so this file is not its own first
/// offender and needs no self-exclusion.</para>
/// </summary>
public class ConfigKeyMigrationTests
{
    /// <summary>The legacy prefix, never written as one literal — see the class remarks.</summary>
    private const string LegacyPrefix = "Gemini" + ":";

    /// <summary>The section Gemini's chat and embedding keys actually live under today.</summary>
    private static readonly string GeminiSection = ChatProviderOptions.SectionFor(ChatProvider.Gemini);

    /// <summary>The needle. The lookbehinds are load-bearing rather than cosmetic.
    ///
    /// <para><c>Ai:</c> + the old prefix is what every migrated site now spells, so a plain
    /// substring search would flag every correct call site and nothing else. What is left to catch
    /// is the prefix standing on its own.</para>
    ///
    /// <para>A preceding <c>.</c> or word character is not a configuration path either: since
    /// EXP-64 the embeddings seam has a <c>case EmbeddingsProvider.Gemini:</c> label, which is a
    /// member access followed by a colon and spells no key at all. Narrowing to "not member access"
    /// is not a loosening — a real leftover is always the quoted legacy prefix with a key name
    /// after it, and the startup throw below covers the half no sweep can see.</para></summary>
    private static readonly Regex LegacyKey =
        new(@"(?<![.\w])(?<!Ai:)" + LegacyPrefix, RegexOptions.Compiled);

    /// <summary>File extensions the sweep reads. Anything that can spell a configuration key:
    /// source, settings, docs, workflows and shell.</summary>
    private static readonly string[] SweptExtensions =
        [".cs", ".json", ".md", ".ts", ".tsx", ".js", ".jsx", ".yml", ".yaml", ".sh", ".props", ".csproj", ".slnx"];

    /// <summary>Directories the sweep never descends into. <c>manuals/</c> is deliberate and named
    /// by the ticket: the ADR and the wayfinder map quote the old keys as history, and rewriting
    /// history to satisfy a lint would destroy the record of what the migration was.
    /// <c>.claude/</c> holds gitignored agent worktrees, which are full checkouts of other branches,
    /// some of them older than the migration. They are not this tree, and sweeping them made the test
    /// red on any machine that had one and green on CI, which has none (EXP-37).</summary>
    private static readonly string[] SkippedDirectories =
        ["manuals", "docs", "node_modules", "bin", "obj", ".git", ".vs", ".claude", "dist", "test-results", "playwright-report"];

    /// <summary>The needle itself, pinned. The sweep above can only ever report what this regex
    /// matches, so narrowing it (EXP-64) has to be shown to still catch the thing it is for — a
    /// sweep that has quietly stopped matching passes in exactly the same silence as a clean tree.
    /// </summary>
    [Theory]
    [InlineData("\"" + LegacyPrefix + "Model\": \"gemini-3.5-flash-lite\"", true)]
    [InlineData("config[\"" + LegacyPrefix + "ApiKey\"]", true)]
    [InlineData("[\"Ai:" + LegacyPrefix + "Model\"] = \"x\"", false)]
    [InlineData("case EmbeddingsProvider.Gemini:", false)]
    [InlineData("ChatProviderOptions.SectionFor(ChatProvider.Gemini)", false)]
    public void The_needle_matches_a_leftover_key_and_nothing_else(string line, bool isOffender)
        => LegacyKey.IsMatch(line).Should().Be(isOffender);

    [Fact]
    public void NoLegacyGeminiConfigKeyRemains()
    {
        var offenders = SourceFiles()
            .Where(f => LegacyKey.IsMatch(File.ReadAllText(f.Absolute)))
            .Select(f => f.Relative)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            $"every '{LegacyPrefix}' configuration key moved to 'Ai:{LegacyPrefix}' (EXP-15). A key left "
            + "behind binds to nothing and silently falls back to its property default. Found: "
            + string.Join(", ", offenders));
    }

    /// <summary>Keeps the sweep honest: one that read nothing, or that had quietly skipped the
    /// trees where the keys actually live, would pass in exactly the same silence.</summary>
    [Fact]
    public void The_sweep_reads_the_source_it_claims_to()
    {
        var files = SourceFiles().ToList();

        files.Should().HaveCountGreaterThan(300, "the api/, tests/, tools/ and web/ trees are comfortably larger than this floor");

        foreach (var expected in new[]
                 {
                     "api/Agents/appsettings.json",
                     "api/Agents/Configuration/AgentsOptions.cs",
                     "api/Agents/Configuration/ChatProviderServiceCollectionExtensions.cs",
                     "api/Infrastructure/Embeddings/EmbeddingOptions.cs",
                     "api/Mcp/appsettings.json",
                     "tools/RetrievalEval/Program.cs",
                     "tests/Agents.Tests/ModelSelectionTests.cs",
                 })
        {
            files.Should().Contain(f => f.Relative == expected,
                $"'{expected}' is one of the migrated sites and the sweep has to be able to see it");
        }

        // And the sweep really does detect the needle it is looking for — asserted against a file
        // it is reading right now rather than a temporary one, so a path bug cannot fake a pass.
        var selfCheck = files.Single(f => f.Relative == "api/Agents/Configuration/AgentsOptions.cs");
        File.ReadAllText(selfCheck.Absolute)
            .Should().Contain("Ai:" + LegacyPrefix, "the migrated site names the new prefix");
    }

    [Fact]
    public void LegacyGeminiSection_ThrowsAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LegacyPrefix + "Model"] = "gemini-3.5-flash-lite",
            })
            .Build();

        var act = () => new ServiceCollection().AddChatProvider(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ai:" + LegacyPrefix + "*",
                "the throw has to name the path that replaced it, not just refuse");
    }

    [Fact]
    public void The_new_section_does_not_trip_the_legacy_guard()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Chat:Provider"] = "Gemini",
                ["Ai:" + LegacyPrefix + "Model"] = "gemini-3.5-flash-lite",
                ["Ai:" + LegacyPrefix + "ApiKey"] = "test-key",
            })
            .Build();

        var act = () => new ServiceCollection().AddChatProvider(config);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The half neither the sweep nor the throw covers: that the section the options class reads
    /// still <em>binds</em>. A rename that unbinds is silent by construction — nothing throws, the
    /// options object falls back to its code defaults, and the host looks healthy while reading
    /// none of the values anyone set.
    ///
    /// <para>Asserted by overriding through the section rather than by reading a literal out of a
    /// settings file, because since EXP-102 the shipped files carry no Gemini chat values at all:
    /// every one of them equalled <see cref="ChatProviderOptions.Defaults"/> and the duplicate was
    /// removed, not the knob. An override that arrives is proof the path is live, and it is proof
    /// the <em>binder</em> accepts it rather than proof one JSON file and one C# method happen to
    /// spell the same string. The chat client itself is not resolved here, because constructing one
    /// needs a real credential (<c>ApiKeyCredential</c> rejects an empty key) — and needing a key
    /// to prove a key name is the wrong trade.</para>
    /// </summary>
    [Theory]
    [InlineData("Model", "gemini-pro-latest")]
    [InlineData("Endpoint", "https://override.example/v1beta/openai")]
    public void The_shipped_section_still_binds_under_the_new_path(string key, string overridden)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "api/Agents/appsettings.json"))
            .AddInMemoryCollection([new KeyValuePair<string, string?>($"{GeminiSection}:{key}", overridden)])
            .Build();

        var options = ChatProviderOptions.Defaults(ChatProvider.Gemini);
        config.GetSection(GeminiSection).Bind(options);

        typeof(ChatProviderOptions).GetProperty(key)!.GetValue(options).Should().Be(overridden,
            $"'{GeminiSection}:{key}' has to be the path the options class reads; a mismatch binds "
            + "to nothing and falls back to the code default in silence");
    }

    /// <summary>
    /// EXP-102, which is EXP-89's "each value on one side" rule applied to the <c>Ai:*</c> blocks it
    /// skipped. Every key the shipped Agents file spelled under <see cref="GeminiSection"/> —
    /// endpoint, model, empty credential — already had exactly that value in
    /// <see cref="ChatProviderOptions.Defaults"/>, where the comment recording <em>why</em> the model
    /// is pinned to an explicit generation also lives. Two copies are free to drift, and when they
    /// do the file wins while the comment goes on describing the other one.
    ///
    /// <para>Neither value is tuned per environment, which is the rule's test for which side wins:
    /// the endpoint is Google's one public OpenAI-compatible host and the model is a free-tier quota
    /// row (P1T-114/P1T-115). Azure's chat block is the deliberate opposite and stays shipped — its
    /// code default is empty, so the file is the only copy, not a second one.</para>
    /// </summary>
    [Fact]
    public void The_shipped_agents_settings_repeat_no_chat_code_default()
    {
        var section = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "api/Agents/appsettings.json"))
            .Build()
            .GetSection(GeminiSection);
        var defaults = ChatProviderOptions.Defaults(ChatProvider.Gemini);

        var repeated = new[]
            {
                (Key: nameof(ChatProviderOptions.Endpoint), FromCode: defaults.Endpoint),
                (Key: nameof(ChatProviderOptions.Model), FromCode: defaults.Model),
                (Key: nameof(ChatProviderOptions.ApiKey), FromCode: defaults.ApiKey),
            }
            .Where(v => section[v.Key] == v.FromCode)
            .Select(v => $"{GeminiSection}:{v.Key}")
            .ToList();

        repeated.Should().BeEmpty(
            "each of these already has exactly this value in ChatProviderOptions.Defaults, so the "
            + "shipped file is a second copy that can drift from the one carrying the reason. "
            + "Found: " + string.Join(", ", repeated));
    }

    /// <summary>
    /// The shipped default chat backend is Azure OpenAI (EXP-41): Gemini's free tier is 500 model
    /// calls a day for the whole project, and EXP-40 measured one roster-qa question costing a user
    /// their whole day. Literals, not property defaults — <see cref="ChatProviderOptions.Defaults"/>
    /// are empty strings for Azure, so an unbound block would look exactly like an unset one.
    /// <see cref="ChatProviderOptions.Model"/> is a DEPLOYMENT name (gpt-4.1-mini sits behind it).
    /// </summary>
    [Fact]
    public void The_shipped_agents_settings_select_the_azure_deployment()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "api/Agents/appsettings.json"))
            .Build();

        config["Ai:Chat:Provider"].Should().Be("AzureFoundry");
        var azure = config
            .GetSection(ChatProviderOptions.SectionFor(ChatProvider.AzureFoundry))
            .Get<ChatProviderOptions>()!;
        azure.Endpoint.Should().Be("https://experttojob-openai-swc.openai.azure.com/openai/v1/");
        azure.Model.Should().Be("gpt-4-1-mini");
        azure.ApiKey.Should().BeEmpty("the key comes from AZURE_FOUNDRY_API_KEY, never a tracked file");
    }

    /// <summary>The two options classes share the section on purpose — chat and embeddings really
    /// do share that endpoint and key (chat ADR §2 decision 3; embeddings ADR §2 decision 2).
    /// Asserted rather than assumed, because the next rename only has to move one of them to break
    /// the pairing quietly. Since EXP-64 the embeddings side is per provider, so it is the Gemini
    /// mapping that has to equal Gemini's chat block.</summary>
    [Fact]
    public void Chat_and_embeddings_read_the_same_section()
    {
        Infrastructure.Embeddings.EmbeddingOptions
            .SectionFor(Infrastructure.Embeddings.EmbeddingsProvider.Gemini)
            .Should().Be(GeminiSection);
        GeminiSection.Should().Be("Ai:" + LegacyPrefix.TrimEnd(':'));
    }

    private static IEnumerable<(string Absolute, string Relative)> SourceFiles()
    {
        var root = RepoRoot();
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => SweptExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !SkippedDirectories.Any(skip =>
                path.Contains($"{separator}{skip}{separator}", StringComparison.OrdinalIgnoreCase)))
            .Select(path => (path, Path.GetRelativePath(root, path).Replace(separator, '/')));
    }

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
                   "Could not find ExpertToJob.slnx above the test binary; the config key sweep cannot run.");
    }
}
