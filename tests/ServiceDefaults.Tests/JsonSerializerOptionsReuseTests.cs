using System.Text.RegularExpressions;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// <c>JsonSerializerOptions.Web</c> (.NET 9+) is the Web defaults, already built and already frozen.
/// A private <c>new(JsonSerializerDefaults.Web)</c> field is a copy of it, and
/// <c>PropertyNameCaseInsensitive = true</c> on top of that sets what the Web defaults already set —
/// so the copy is not a customisation, just another instance with its own caches. EXP-84 removed 21
/// of them; this test is what keeps the twenty-second from being written. It reads the repo's own
/// source rather than reflecting over the assemblies, because a field is only visible as a field in
/// the text that declares it.
///
/// An options object that actually customises something (a converter, an indent, a resolver) is not
/// an offender and never shows up here: the rule below only fires on a construction whose initializer
/// is empty or sets nothing but case-insensitivity.
/// </summary>
public class JsonSerializerOptionsReuseTests
{
    /// <summary>
    /// Matches <c>new(JsonSerializerDefaults.Web)</c> in each of its spellings — target-typed, named,
    /// and fully qualified, assigned to a field or passed inline — and captures the object
    /// initializer that follows, if there is one, so it can be read. The capture runs to the first
    /// <c>}</c>, which is enough: an initializer that nests a brace is setting something, and
    /// setting anything is what takes a site out of scope here.
    /// </summary>
    private static readonly Regex WebDefaultsConstruction = new(
        @"new(?:\s+(?:System\.Text\.Json\.)?JsonSerializerOptions)?\s*\(\s*(?:System\.Text\.Json\.)?JsonSerializerDefaults\.Web\s*\)(?<initializer>\s*\{[^}]*\})?",
        RegexOptions.Compiled);

    /// <summary>
    /// An initializer, with all whitespace stripped, that adds nothing to the Web defaults.
    /// </summary>
    private static readonly string[] RedundantInitializers =
    [
        "",
        "{PropertyNameCaseInsensitive=true}",
        "{PropertyNameCaseInsensitive=true,}",
    ];

    /// <summary>
    /// The one redundant copy EXP-84 left standing. It is out of that issue's enumerated scope — the
    /// ticket listed 21 sites and never saw this one — and is tracked separately rather than removed
    /// here. Any other path appearing in the failure message is a new copy, not an inherited one.
    /// </summary>
    private static readonly string[] KnownRemaining =
    [
        Path.Combine("api", "Web", "Controllers", "AuthController.cs"),
    ];

    [Fact]
    public void No_production_file_copies_the_web_defaults_into_its_own_options()
    {
        var root = RepoRoot();

        var offenders = ProductionSources(root)
            .SelectMany(file => WebDefaultsConstruction
                .Matches(File.ReadAllText(file))
                .Where(m => RedundantInitializers.Contains(Strip(m.Groups["initializer"].Value), StringComparer.Ordinal))
                .Select(_ => Path.GetRelativePath(root, file)))
            .Where(path => !KnownRemaining.Contains(path, StringComparer.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a copy of the Web defaults is what JsonSerializerOptions.Web already is — pass it instead. Found: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The other half of the rule: an options object that customises something must keep its own
    /// instance, so the sweep above is never "fixed" by deleting one of these. The literal each site
    /// is pinned by is the customisation itself — the thing that makes it not a copy.
    /// </summary>
    [Theory]
    [InlineData("api/Mcp/Program.cs", "TypeInfoResolver = new DefaultJsonTypeInfoResolver(),")]
    [InlineData("api/Web/Controllers/TransparencyController.cs", "new(JsonSerializerDefaults.Web) { WriteIndented = true };")]
    [InlineData("api/Infrastructure/Persistence/SeedData/DemoRosterLoader.cs", "UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,")]
    public void Customised_options_keep_their_own_instance(string relativePath, string customisation)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        text.Should().Contain(
            customisation,
            $"{relativePath} customises its options with something the Web defaults do not set, so it keeps its own instance");
    }

    /// <summary>Hand-written C# under <c>api/</c> and <c>tools/</c>: build output and generated
    /// sources are not source anyone can fix.</summary>
    private static IEnumerable<string> ProductionSources(string root) =>
        new[] { "api", "tools" }
            .Select(area => Path.Combine(root, area))
            .SelectMany(area => Directory.EnumerateFiles(area, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static string Strip(string initializer) =>
        string.Concat(initializer.Where(c => !char.IsWhiteSpace(c)));

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
                   "Could not find ExpertToJob.slnx above the test binary; the JsonSerializerOptions sweep cannot run.");
    }
}
