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

    /// <summary>The needle. The lookbehind is load-bearing rather than cosmetic: the new prefix
    /// <c>Ai:</c> + the old one is what every migrated site now spells, so a plain substring search
    /// would flag every correct call site and nothing else. What is left to catch is the prefix
    /// standing on its own.</summary>
    private static readonly Regex LegacyKey = new("(?<!Ai:)" + LegacyPrefix, RegexOptions.Compiled);

    /// <summary>File extensions the sweep reads. Anything that can spell a configuration key:
    /// source, settings, docs, workflows and shell.</summary>
    private static readonly string[] SweptExtensions =
        [".cs", ".json", ".md", ".ts", ".tsx", ".js", ".jsx", ".yml", ".yaml", ".sh", ".props", ".csproj", ".slnx"];

    /// <summary>Directories the sweep never descends into. <c>manuals/</c> is deliberate and named
    /// by the ticket: the ADR and the wayfinder map quote the old keys as history, and rewriting
    /// history to satisfy a lint would destroy the record of what the migration was.</summary>
    private static readonly string[] SkippedDirectories =
        ["manuals", "docs", "node_modules", "bin", "obj", ".git", ".vs", "dist", "test-results", "playwright-report"];

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
    /// The half neither the sweep nor the throw covers: that the shipped settings files still
    /// <em>bind</em>. A rename that unbinds is silent by construction — nothing throws, the options
    /// object falls back to its property defaults, and the host looks healthy while reading none of
    /// the values anyone set.
    ///
    /// <para>Each value is asserted against the literal in its settings file, never against the
    /// matching property default: those two agreeing is exactly what would hide the failure. The
    /// chat client itself is not resolved here, because constructing one needs a real credential
    /// (<c>ApiKeyCredential</c> rejects an empty key) — and needing a key to prove a key name is
    /// the wrong trade.</para>
    /// </summary>
    [Theory]
    [InlineData("api/Agents/appsettings.json", "Model", "gemini-3.5-flash-lite")]
    [InlineData("api/Agents/appsettings.json", "Endpoint", "https://generativelanguage.googleapis.com/v1beta/openai")]
    [InlineData("api/Mcp/appsettings.json", "EmbeddingModel", "gemini-embedding-001")]
    [InlineData("api/Mcp/appsettings.json", "Endpoint", "https://generativelanguage.googleapis.com/v1beta/openai")]
    public void The_shipped_settings_bind_under_the_new_section(string settingsFile, string key, string expected)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), settingsFile))
            .Build();

        config[$"{GeminiOptions.Section}:{key}"].Should().Be(expected,
            $"'{settingsFile}' has to spell the path the options class reads ('{GeminiOptions.Section}'); "
            + "a mismatch binds to nothing and falls back to the property default in silence");
    }

    /// <summary>The two options classes share the section on purpose — chat and embeddings really
    /// do share that endpoint and key (ADR §2 decision 3). Asserted rather than assumed, because
    /// the next rename only has to move one of them to break the pairing quietly.</summary>
    [Fact]
    public void Chat_and_embeddings_read_the_same_section()
    {
        Infrastructure.Embeddings.EmbeddingOptions.Section.Should().Be(GeminiOptions.Section);
        GeminiOptions.Section.Should().Be("Ai:" + LegacyPrefix.TrimEnd(':'));
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
