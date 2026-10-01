using System.Xml.Linq;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The two halves of Central Package Management have to stay paired (P1T-222): a csproj names a
/// package and <c>Directory.Packages.props</c> names its version. Neither half fails loudly on its
/// own when the other moves. A reference with no <c>PackageVersion</c> is NU1008 at restore, which
/// is at least a build error — but a <c>PackageVersion</c> with no reference is nothing at all: the
/// entry simply sits there, read as a dependency this repo has when it does not, and maintained
/// (bumped, CVE-checked, argued about in review) for a package nothing resolves.
///
/// <para>Written for EXP-88, which removed four direct references that were already arriving
/// transitively or were never used. Removing a reference and leaving its version behind is the
/// exact mistake this catches, and it is invisible in a green build.</para>
/// </summary>
public class CentralPackageManagementTests
{
    [Fact]
    public void Every_central_version_is_claimed_by_at_least_one_project()
    {
        var orphans = CentralVersions()
            .Except(DeclaredReferences().Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            "Directory.Packages.props sets versions for dependencies this repo takes; an entry no "
            + "csproj references is a version nothing resolves. Found: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Every_referenced_package_takes_its_version_from_the_central_list()
    {
        var versions = CentralVersions();
        var unversioned = DeclaredReferences()
            .Where(r => !versions.Contains(r.Key))
            .Select(r => $"{r.Key} (in {string.Join(", ", r.Value)})")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        unversioned.Should().BeEmpty(
            "every PackageReference resolves its version from Directory.Packages.props. Found: "
            + string.Join(", ", unversioned));
    }

    /// <summary>
    /// CPM centralises versions, not dependencies — so a <c>Version=</c> on a reference silently
    /// wins over the central list for that one project, which is how two projects come to compile
    /// against two versions of the same package while the list says otherwise.
    /// </summary>
    [Fact]
    public void No_project_pins_a_version_on_its_own_reference()
    {
        var pinned = ProjectFiles()
            .SelectMany(p => XDocument.Load(p.Absolute)
                .Descendants("PackageReference")
                .Where(r => r.Attribute("Version") is not null || r.Element("Version") is not null)
                .Select(r => $"{r.Attribute("Include")?.Value} (in {p.Relative})"))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        pinned.Should().BeEmpty(
            "a PackageReference names a package and never a version (P1T-222). Found: "
            + string.Join(", ", pinned));
    }

    private static HashSet<string> CentralVersions() =>
        XDocument.Load(Path.Combine(RepoRoot(), "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(v => v.Attribute("Include")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Package id to the projects that reference it.</summary>
    private static Dictionary<string, List<string>> DeclaredReferences()
    {
        var declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in ProjectFiles())
        {
            foreach (var id in XDocument.Load(project.Absolute)
                         .Descendants("PackageReference")
                         .Select(r => r.Attribute("Include")?.Value)
                         .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!declared.TryGetValue(id!, out var projects))
                {
                    declared[id!] = projects = [];
                }

                projects.Add(project.Relative);
            }
        }

        return declared;
    }

    private static IEnumerable<(string Absolute, string Relative)> ProjectFiles()
    {
        var root = RepoRoot();
        var separator = Path.DirectorySeparatorChar;

        return Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                        && !p.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (p, Path.GetRelativePath(root, p).Replace(separator, '/')))
            .ToList();
    }

    /// <summary>Walks up from the test binary until the solution file appears.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the package-management check cannot run.");
    }
}
