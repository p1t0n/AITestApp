using ExpertToJob.Application.Visibility;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The structural half of "one seam, two predicates" (P1T-185). The behavioural half — every
/// consumer, seen and unseen — is proven against Postgres in <c>Mcp.Tests/RosterVisibilityTests</c>;
/// what those tests cannot prove is that the <em>next</em> consumer will be covered too.
///
/// <para>So this reads the source. A second <c>HiddenAt</c> in a query somewhere is how a
/// blast-radius list stops being true: the first copy is written carefully, the second is written
/// by somebody who did not know the first existed, and the third quietly disagrees with both. The
/// allow-list below is the seam plus the places a column has to be mentioned to exist at all, and
/// adding to it should feel like a decision.</para>
/// </summary>
public class VisibilitySeamTests
{
    /// <summary>Where the predicate may be written, and why each one is allowed to.</summary>
    private static readonly Dictionary<string, string> MayMentionHiddenAt = new()
    {
        ["api/Domain/Entities/Expert.cs"] = "the column itself",
        ["api/Application/Visibility/RosterVisibility.cs"] = "the seam — the one place the predicate is written",
        ["api/Application/Visibility/ExpertVisibilityService.cs"] = "the pause control: the only writer",
        ["api/Application/Experts/ExpertDtos.cs"] = "read back so a Service Manager sees the badge",
        ["api/Application/Experts/ExpertMappings.cs"] = "the projection that fills that field",
        ["api/Application/Compliance/AccessAndExportService.cs"] =
            "disclosure, not filtering: the Art. 15 view owes the person 'paused since when' "
            + "(P1T-185 §2, P1T-187). It reads the timestamp and never predicates on it.",
    };

    [Fact]
    public void HiddenAt_is_queried_in_exactly_one_place()
    {
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f.Absolute).Contains("HiddenAt"))
            .Select(f => f.Relative)
            .Where(relative => !MayMentionHiddenAt.ContainsKey(relative))
            .OrderBy(x => x)
            .ToList();

        offenders.Should().BeEmpty(
            "the visibility predicate lives in RosterVisibility and nowhere else — a second filter " +
            "bolted onto another query path is how the blast-radius list in " +
            "manuals/expert-visibility.md stops being true. If one of these genuinely needs the " +
            "column, add it to MayMentionHiddenAt with a reason. Found: " + string.Join(", ", offenders));
    }

    /// <summary>Keeps the check above honest: a sweep that found no files would pass it in silence,
    /// and so would one whose allow-list had drifted off the real paths.</summary>
    [Fact]
    public void The_sweep_reads_the_source_it_claims_to()
    {
        var files = SourceFiles().ToList();

        files.Should().HaveCountGreaterThan(100, "the api/ tree is comfortably larger than this floor");

        foreach (var (path, reason) in MayMentionHiddenAt)
        {
            files.Should().Contain(f => f.Relative == path, $"the allow-list entry '{reason}' names a real file");
            File.ReadAllText(files.Single(f => f.Relative == path).Absolute)
                .Should().Contain("HiddenAt", $"'{path}' is allow-listed but no longer mentions the column");
        }
    }

    /// <summary>
    /// The two predicates are separate, and the pairing matters: the scan's population is a strict
    /// subset of the bench's. Asserted on the expressions rather than on a query, because the point
    /// is that <c>Scannable</c> is <em>composed from</em> the bench predicate and not a second copy
    /// of it — an independent copy is exactly what would drift.
    /// </summary>
    [Fact]
    public void Scannable_is_the_bench_predicate_plus_the_art22_route()
    {
        var seam = File.ReadAllText(Path.Combine(
            RepoRoot(), "api/Application/Visibility/RosterVisibility.cs"));

        seam.Should().Contain("experts.OnTheBench().Where(HasArt22Route)",
            "the scan population is the bench population narrowed, never a parallel definition");

        // And the predicates themselves are reachable as expressions, so a caller composes them
        // rather than retyping them.
        RosterVisibility.NotHidden.Should().NotBeNull();
        RosterVisibility.HasArt22Route.Should().NotBeNull();
    }

    private static IEnumerable<(string Absolute, string Relative)> SourceFiles()
    {
        var root = RepoRoot();
        var api = Path.Combine(root, "api");

        return Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           // Migrations are the schema: the column has to appear there to exist.
                           && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Select(path => (path, Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')));
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
                   "Could not find ExpertToJob.slnx above the test binary; the source sweep cannot run.");
    }
}
