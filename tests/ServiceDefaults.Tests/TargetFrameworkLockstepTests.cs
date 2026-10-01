using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The retarget to .NET 11 (EXP-73) is spread across three files that nothing else holds together:
/// every csproj names the target framework, <c>global.json</c> names the SDK that compiles it, and
/// <c>.github/workflows/ci.yml</c> names the SDK CI installs. Drift between them does not fail at
/// restore — it fails as one project silently left on the old framework, or as a CI runner that
/// resolves a different SDK from the one the repo is pinned to. The literals below are asserted as
/// literals on purpose: this is a freeze, so a deliberate move edits the test, and an accidental one
/// is caught. <c>manuals/adr-dotnet-11-on-rc.md</c> records why the RC is the pin.
/// </summary>
public class TargetFrameworkLockstepTests
{
    private const string TargetFramework = "net11.0";
    private const string SdkVersion = "11.0.100-rc.1.26425.128";

    [Fact]
    public void Every_project_in_the_repo_targets_the_same_single_framework()
    {
        var root = RepoRoot();

        var projects = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        projects.Should().HaveCount(24, "the solution has 24 projects across api/, tests/ and tools/");

        var offenders = projects
            .Select(p => (Path: Path.GetRelativePath(root, p), Document: XDocument.Load(p)))
            .Select(p => (
                p.Path,
                Single: p.Document.Descendants("TargetFramework").Select(e => e.Value).SingleOrDefault(),
                Multi: p.Document.Descendants("TargetFrameworks").Select(e => e.Value).SingleOrDefault()))
            .Where(p => p.Single != TargetFramework || p.Multi is not null)
            .ToList();

        offenders.Should().BeEmpty(
            $"every project targets exactly one framework, {TargetFramework}, and none multi-targets. Found: "
            + string.Join(", ", offenders.Select(o => $"{o.Path} -> TargetFramework={o.Single ?? "(none)"} TargetFrameworks={o.Multi ?? "(none)"}")));
    }

    /// <summary>
    /// C# 15 comes from the target framework. A <c>LangVersion</c> anywhere would either pin the
    /// language below what the TFM grants or float it above what the SDK supports; both are a way
    /// to be surprised by a compiler error that the framework bump was supposed to settle.
    /// </summary>
    [Fact]
    public void No_project_pins_a_language_version()
    {
        var root = RepoRoot();

        var pinned = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => XDocument.Load(p).Descendants("LangVersion").Any())
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();

        pinned.Should().BeEmpty(
            "the language version follows the target framework; found LangVersion in: " + string.Join(", ", pinned));
    }

    [Fact]
    public void Global_json_pins_the_sdk_the_target_framework_needs()
    {
        var root = RepoRoot();

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");

        sdk.GetProperty("version").GetString().Should().Be(
            SdkVersion,
            "the repo compiles against the .NET 11 RC SDK; a machine resolving an older SDK cannot build net11.0 at all");
        sdk.GetProperty("allowPrerelease").GetBoolean().Should().BeTrue(
            "the pin names a prerelease SDK, which is only resolvable with allowPrerelease");
        sdk.GetProperty("rollForward").GetString().Should().Be(
            "latestPatch",
            "patches of the pinned feature band are fine; a feature-band jump is a decision, not a side effect");
    }

    [Fact]
    public void Ci_installs_the_same_sdk_band_in_every_job_that_builds()
    {
        var root = RepoRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github/workflows/ci.yml"));

        var versions = Regex.Matches(workflow, @"dotnet-version:\s*'([^']+)'").Select(m => m.Groups[1].Value).ToList();
        var qualities = Regex.Matches(workflow, @"dotnet-quality:\s*'([^']+)'").Select(m => m.Groups[1].Value).ToList();

        versions.Should().HaveCount(2, "the test and e2e jobs each install the SDK");
        versions.Should().OnlyContain(v => v == "11.0.x", "both jobs build net11.0; found: " + string.Join(", ", versions));
        qualities.Should().HaveCount(2, "11.0.x has no stable release yet, so each setup-dotnet needs a quality");
        qualities.Should().OnlyContain(
            q => q == "preview",
            "the pinned SDK is a release candidate, which setup-dotnet only serves under the preview quality; found: "
            + string.Join(", ", qualities));
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
                   "Could not find ExpertToJob.slnx above the test binary; the target-framework lockstep check cannot run.");
    }
}
