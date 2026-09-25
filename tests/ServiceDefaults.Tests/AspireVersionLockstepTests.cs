using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The one version pin Central Package Management cannot reach. The AppHost names its SDK in an
/// attribute — <c>Sdk="Aspire.AppHost.Sdk/&lt;version&gt;"</c> — and every <c>Aspire.Hosting.*</c>
/// package lives in <c>Directory.Packages.props</c>. The two must be the same release: mixed, the
/// build and restore are both clean and the AppHost throws at <em>startup</em>
/// (<c>MissingMethodException</c> / <c>TypeLoadException</c>, microsoft/aspire#13636; the Aspire 13.5
/// release notes list it as a known issue). Until now a comment and
/// <c>manuals/adr-aspire-apphost.md</c> held them together; this holds them at <c>dotnet test</c>.
/// </summary>
public class AspireVersionLockstepTests
{
    [Fact]
    public void AppHost_sdk_and_every_Aspire_Hosting_package_are_the_same_release()
    {
        var root = RepoRoot();

        var csproj = File.ReadAllText(Path.Combine(root, "api/AppHost/ExpertToJob.AppHost.csproj"));
        var sdk = Regex.Match(csproj, @"Sdk=""Aspire\.AppHost\.Sdk/([^""]+)""");
        sdk.Success.Should().BeTrue("the AppHost pins its SDK version in the Project Sdk attribute");

        var hosting = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(e => (Id: (string?)e.Attribute("Include") ?? "", Version: (string?)e.Attribute("Version") ?? ""))
            .Where(p => p.Id.StartsWith("Aspire.Hosting.", StringComparison.Ordinal))
            .ToList();

        hosting.Should().NotBeEmpty("the AppHost takes its integrations from Directory.Packages.props");
        hosting.Should().OnlyContain(
            p => p.Version == sdk.Groups[1].Value,
            $"Aspire.AppHost.Sdk is {sdk.Groups[1].Value}, and a mixed set fails at AppHost startup, not at build. "
            + "Move the Sdk attribute and every Aspire.Hosting.* PackageVersion together. Found: "
            + string.Join(", ", hosting.Select(p => $"{p.Id} {p.Version}")));
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
                   "Could not find ExpertToJob.slnx above the test binary; the Aspire lockstep check cannot run.");
    }
}
