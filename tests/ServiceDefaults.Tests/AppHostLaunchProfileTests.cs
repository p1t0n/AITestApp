using System.Text.Json;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The AppHost must start in Development, and the only thing that says so is a file nobody looks at
/// (EXP-92).
///
/// <para><b>Why this is worth a test.</b> .NET loads user-secrets in Development and nowhere else.
/// <c>dotnet run --project api/AppHost</c> — the one command CLAUDE.md and the verify skill give —
/// defaults to Production when the project has no launch profile, so
/// <c>Parameters:azure-foundry-api-key</c>, the one secret the README tells a developer to set, is
/// silently not read. Nothing fails at startup: the AppHost forwards
/// <c>AZURE_FOUNDRY_API_KEY=""</c> to Mcp and Agents, and the stack comes up looking healthy. The
/// bill arrives later and somewhere else — <c>/agents/shortlist</c> answers 502 "the semantic search
/// backend is unavailable", staffing stops after its shortlist step, and the chat client fails on an
/// empty credential. A missing file that degrades three hosts at a distance is exactly the kind of
/// absence a freeze test exists to notice.</para>
/// </summary>
public class AppHostLaunchProfileTests
{
    [Fact]
    public void The_profile_dotnet_run_picks_starts_the_AppHost_in_Development()
    {
        var profiles = ProjectProfiles();

        profiles.Should().NotBeEmpty(
            "'dotnet run --project api/AppHost' runs the first commandName=Project profile, and "
            + "with none it falls back to Production, where user-secrets are not loaded");

        var (name, profile) = profiles[0];

        EnvironmentOf(profile).Should().Be("Development",
            $"'{name}' is the profile 'dotnet run --project api/AppHost' picks, and only in "
            + "Development does the AppHost read the azure-foundry-api-key user-secret it forwards "
            + "to Mcp and Agents");
    }

    /// <summary>A second profile added later is the way this regresses without anyone touching the
    /// first one: <c>dotnet run</c> picks by order, and reordering is a one-line diff that reads
    /// like nothing.</summary>
    [Fact]
    public void No_launch_profile_starts_the_AppHost_outside_Development()
    {
        foreach (var (name, profile) in ProjectProfiles())
        {
            EnvironmentOf(profile).Should().Be("Development",
                $"'{name}' can be the profile a developer or 'dotnet run' picks, and the AppHost "
                + "has no reason to be launched outside Development from the repo");
        }
    }

    private static string? EnvironmentOf(JsonElement profile)
        => profile.TryGetProperty("environmentVariables", out var env)
           && env.TryGetProperty("DOTNET_ENVIRONMENT", out var value)
            ? value.GetString()
            : null;

    /// <summary>The launch profiles that actually start the project, in file order — which is the
    /// order <c>dotnet run</c> selects from.</summary>
    private static List<(string Name, JsonElement Profile)> ProjectProfiles()
    {
        var path = Path.Combine(RepoRoot(), "api/AppHost/Properties/launchSettings.json");

        File.Exists(path).Should().BeTrue(
            "api/AppHost/Properties/launchSettings.json is what gives 'dotnet run' an environment; "
            + "without it the AppHost starts in Production and skips user-secrets (EXP-92)");

        // Deserialized rather than JsonDocument.Parse'd: a JsonElement from a document is only
        // valid while that document lives, and the profiles outlive this method.
        var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));

        root.TryGetProperty("profiles", out var profiles).Should().BeTrue(
            "a launchSettings.json with no 'profiles' object is the same as no file at all");

        return profiles.EnumerateObject()
            .Where(p => p.Value.TryGetProperty("commandName", out var command)
                        && command.GetString() == "Project")
            .Select(p => (p.Name, p.Value))
            .ToList();
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
                   "Could not find ExpertToJob.slnx above the test binary; the AppHost launch profile check cannot run.");
    }
}
