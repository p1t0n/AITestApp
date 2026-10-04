using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// The leak guard for tracked configuration (EXP-118, decided in EXP-112 item 6). This repository
/// is <strong>public</strong>, and this test exists because a real Azure Foundry key once sat in
/// <c>api/Agents/appsettings.json</c> — uncommitted, which is the only reason it is not in the
/// history. Nothing in the build would have objected if it had been staged.
///
/// <para>What makes a leak quiet is that the diff looks like configuration. A credential pasted
/// into a settings file is one line, in a file full of lines that belong there, next to an endpoint
/// and a model name. Review catches it when someone happens to look; this catches it every run.</para>
///
/// <para><strong>It reads the git index, not the working copy.</strong> Two reasons, and they pull
/// the same way. A developer running the stack locally pastes a key into
/// <c>api/Agents/appsettings.json</c> to try something — that edit is theirs, it never leaves the
/// machine, and a guard that went red on it would be trained away within a week. But the moment
/// that edit is <c>git add</c>-ed it is a credential one <c>commit</c> away from a public remote,
/// which is precisely when the guard should shout. The index is the line between those two, so the
/// index is what it reads. (The ticket offered <c>git show HEAD:&lt;path&gt;</c>; the index is the
/// same idea one step earlier, and it also handles a settings file added but not yet committed,
/// which has no blob in <c>HEAD</c> at all.)</para>
/// </summary>
public class TrackedSecretsTests
{
    /// <summary>
    /// Key-name fragments that mark a value as credential-bearing, matched case-insensitively
    /// against every segment of the key path. Deliberately broad: a false positive costs one
    /// allow-list entry and a sentence explaining it, a false negative costs a key rotation.
    /// </summary>
    private static readonly string[] SecretFragments =
        ["apikey", "signingkey", "secret", "password", "token", "credential"];

    /// <summary>
    /// A bare <c>Key</c> segment is a secret only under one of these — on its own the word is far
    /// too common in configuration (<c>Key</c> as a dictionary discriminator, a partition key, a
    /// cache key) to mean a credential. Under <c>Jwt</c> it is the signing material.
    /// </summary>
    private static readonly string[] BareKeyParents = ["jwt", "auth"];

    /// <summary>
    /// Values that carry a credential inline rather than in their key name — a connection string
    /// spells <c>Password=</c> no matter what the key above it is called.
    /// </summary>
    private const string InlinePasswordMarker = "Password=";

    /// <summary>
    /// The only non-empty credential-shaped values allowed to be tracked, pinned to the exact file,
    /// key path <em>and</em> value. Pinning the value is the point: an allow-list keyed on the path
    /// alone would wave through whatever that path grows into, which is the one thing this test is
    /// here to stop. Swap any of these for a real credential and the entry stops matching, so the
    /// guard trips again.
    /// </summary>
    private static readonly AllowedValue[] AllowList =
    [
        // The three hosts' local-development connection string: the AppHost's own Postgres
        // container, on localhost, with the postgres/postgres pair that every pgvector image ships
        // with. It is a default, not a credential — the deployed hosts take ConnectionStrings:Default
        // from the environment, and nothing reachable from outside the machine ever accepts it.
        new("api/Agents/appsettings.json", "ConnectionStrings:Default", LocalPostgres),
        new("api/Mcp/appsettings.json", "ConnectionStrings:Default", LocalPostgres),
        new("api/Web/appsettings.json", "ConnectionStrings:Default", LocalPostgres),
    ];

    private const string LocalPostgres =
        "Host=localhost;Port=5432;Database=experttojob;Username=postgres;Password=postgres";

    /// <summary>
    /// The guard. Every tracked <c>appsettings*.json</c> under <c>api/</c> and <c>tools/</c>, read
    /// out of the index, minus the <c>*.Development.json</c> carve-out below.
    /// </summary>
    [Fact]
    public void No_tracked_settings_file_carries_a_credential()
    {
        var offenders = ShippedSettingsFiles()
            .SelectMany(file => Findings(file.Path, file.Json))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "this repository is public and a credential in a tracked settings file is published the "
            + "moment the branch is. Move it to AZURE_FOUNDRY_API_KEY, a user-secret or the "
            + "deployment's own configuration, and leave the key here empty. Found: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The carve-out, and the proof it is load-bearing rather than decorative. The
    /// <c>*.Development.json</c> files really do carry non-empty credential-shaped values — the
    /// insecure JWT signing key and the eight agent client secrets that match the realm import —
    /// so a carve-out that had stopped applying would be noticed here, and a carve-out protecting
    /// nothing would be noticed here too.
    /// </summary>
    [Fact]
    public void Development_files_are_carved_out_and_the_carve_out_is_needed()
    {
        var devFiles = TrackedSettingsFiles().Where(f => IsDevelopmentOverlay(f.Path)).ToList();

        devFiles.Select(f => f.Path).Should().BeEquivalentTo(
            ["api/Agents/appsettings.Development.json", "api/Web/appsettings.Development.json"],
            "these are the dev overlays the carve-out is written for");

        foreach (var file in devFiles)
        {
            Findings(file.Path, file.Json).Should().NotBeEmpty(
                $"'{file.Path}' holds dev placeholders that the guard would report if it scanned "
                + "them, which is what makes the carve-out worth having rather than decoration");
        }

        ShippedSettingsFiles().Select(f => f.Path).Should().NotIntersectWith(devFiles.Select(f => f.Path));
    }

    /// <summary>Red first: the planted key the guard exists for, and the shape of what it says
    /// about it. The value never appears in the report — a failing CI log is as public as the
    /// repository, so a guard that printed the credential it found would publish it itself.</summary>
    [Fact]
    public void A_planted_credential_is_reported_by_file_and_key_path_and_never_by_value()
    {
        const string planted = "sk-planted-credential-value";
        var json = Parse($$"""
            {
              "Ai": {
                "AzureFoundry": { "Endpoint": "https://example.invalid/", "ApiKey": "{{planted}}" }
              }
            }
            """);

        var findings = Findings("api/Agents/appsettings.json", json);

        findings.Should().ContainSingle()
            .Which.Should().Be("api/Agents/appsettings.json: Ai:AzureFoundry:ApiKey");
        findings.Should().NotContain(f => f.Contains(planted, StringComparison.Ordinal));
    }

    /// <summary>The detector itself, pinned. Everything above can only report what this decides,
    /// so a detector that had quietly stopped matching would pass in the same silence as a clean
    /// tree — the failure mode <c>ConfigKeyMigrationTests</c> names for its own needle.</summary>
    [Theory]
    // The key names a credential, at any depth and in any casing.
    [InlineData("Ai:AzureFoundry:ApiKey", true)]
    [InlineData("Ai:Gemini:apikey", true)]
    [InlineData("Auth:Jwt:SigningKey", true)]
    [InlineData("Auth:Jwt:Key", true)]
    [InlineData("McpAuth:roster-qa:ClientSecret", true)]
    [InlineData("Postgres:Password", true)]
    [InlineData("GitHub:AccessToken", true)]
    // ...and the things that merely sound like one.
    [InlineData("Ai:AzureFoundry:Endpoint", false)]
    [InlineData("McpAuth:roster-qa:ClientId", false)]
    [InlineData("Auth:Passkey:ServerDomain", false)]
    [InlineData("Keycloak:Realm", false)]
    // A bare 'Key' outside an auth section is a dictionary key, not a credential.
    [InlineData("Telemetry:Key", false)]
    // Named a secret, but the value is a number: Usage and AgentBudgets are full of these, and a
    // JSON number is not a credential. The number carve-out is in the walk, not here — the name
    // really does look like a secret, which is why it is worth saying so.
    [InlineData("Usage:DefaultDailyTokens", true)]
    [InlineData("AgentBudgets:Default:MaxToolResultTokens", true)]
    public void The_detector_matches_a_credential_key_and_nothing_else(string keyPath, bool isSecret)
        => IsSecretBearingKey(keyPath).Should().Be(isSecret);

    /// <summary>A value can carry a credential under an innocent key — the connection-string case,
    /// which is why the allow-list has three entries and not none.</summary>
    [Theory]
    [InlineData("ConnectionStrings:Default", "Host=db;Username=app;Password=hunter2", true)]
    [InlineData("ConnectionStrings:Default", "Host=db;Username=app", false)]
    [InlineData("McpServer:BaseUrl", "http://localhost:5100", false)]
    public void An_inline_password_is_caught_whatever_the_key_is_called(string keyPath, string value, bool isSecret)
        => IsSecretBearingValue(keyPath, value).Should().Be(isSecret);

    /// <summary>
    /// Keeps the allow-list honest in both directions. An entry whose file, path or value has moved
    /// is stale, and a stale entry is a hole: it waves through nothing it was written for while
    /// still reading, to the next person, like a reviewed exception.
    /// </summary>
    [Fact]
    public void Every_allow_list_entry_still_matches_exactly_what_it_excuses()
    {
        var shipped = ShippedSettingsFiles().ToDictionary(f => f.Path, f => f.Json);

        foreach (var allowed in AllowList)
        {
            shipped.Should().ContainKey(allowed.File);
            var actual = Leaves(shipped[allowed.File])
                .Where(leaf => leaf.KeyPath == allowed.KeyPath)
                .Select(leaf => leaf.Value)
                .ToList();

            actual.Should().Equal([allowed.Value],
                $"the allow-list excuses '{allowed.KeyPath}' in '{allowed.File}' for exactly the "
                + "value documented beside it; anything else has to be re-read, not inherited");
        }
    }

    /// <summary>Keeps the sweep honest: one that read no files, or that had stopped seeing the
    /// tree where the settings actually live, would also report no offenders.</summary>
    [Fact]
    public void The_sweep_reads_the_tracked_settings_it_claims_to()
    {
        TrackedSettingsFiles().Select(f => f.Path).Should().BeEquivalentTo(
        [
            "api/Agents/appsettings.Development.json",
            "api/Agents/appsettings.json",
            "api/Mcp/appsettings.json",
            "api/Web/appsettings.Development.json",
            "api/Web/appsettings.json",
        ], "every tracked appsettings file under api/ and tools/ is in scope; a new host's file "
           + "joins this list rather than slipping past the guard unnoticed");

        // And the content really is the indexed blob rather than an empty read.
        var agents = ShippedSettingsFiles().Single(f => f.Path == "api/Agents/appsettings.json");
        Leaves(agents.Json).Should().Contain(leaf =>
            leaf.KeyPath == "Ai:AzureFoundry:Model" && leaf.Value == "gpt-4-1-mini");
    }

    // ---- the guard itself -------------------------------------------------------------------

    /// <summary>Credential-shaped leaves of one parsed settings file, as "<c>file: key:path</c>"
    /// strings. Never carries the value.</summary>
    private static IEnumerable<string> Findings(string path, JsonDocument json) =>
        Leaves(json)
            .Where(leaf => IsSecretBearingKey(leaf.KeyPath) || IsSecretBearingValue(leaf.KeyPath, leaf.Value))
            .Where(leaf => leaf.Value.Length > 0)
            .Where(leaf => !AllowList.Any(a =>
                a.File == path && a.KeyPath == leaf.KeyPath && a.Value == leaf.Value))
            .Select(leaf => $"{path}: {leaf.KeyPath}");

    private static bool IsSecretBearingKey(string keyPath)
    {
        var segments = keyPath.Split(':');
        var last = segments[^1];

        if (SecretFragments.Any(f => last.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return last.Equals("Key", StringComparison.OrdinalIgnoreCase)
               && segments[..^1].Any(s => BareKeyParents.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsSecretBearingValue(string keyPath, string value)
        => value.Contains(InlinePasswordMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every string leaf of the document, with its configuration key path. <strong>String</strong>
    /// leaves only: a credential in JSON is a string, and the alternative is an allow-list of a
    /// dozen token <em>budgets</em> (<c>Usage:DefaultDailyTokens</c>, <c>MaxToolResultTokens</c>, …)
    /// whose key names match "token" and whose values are integers. Excusing those one by one would
    /// make the allow-list mostly noise, and an allow-list nobody reads is where a real exception
    /// goes to hide.
    /// </summary>
    private static IEnumerable<Leaf> Leaves(JsonDocument json) => Leaves(json.RootElement, prefix: "");

    private static IEnumerable<Leaf> Leaves(JsonElement element, string prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}";
                    foreach (var leaf in Leaves(property.Value, path))
                    {
                        yield return leaf;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var leaf in Leaves(item, $"{prefix}:{index}"))
                    {
                        yield return leaf;
                    }

                    index++;
                }

                break;

            case JsonValueKind.String:
                yield return new Leaf(prefix, element.GetString() ?? "");
                break;
        }
    }

    // ---- reading the index ------------------------------------------------------------------

    private static IEnumerable<SettingsFile> ShippedSettingsFiles() =>
        TrackedSettingsFiles().Where(f => !IsDevelopmentOverlay(f.Path));

    /// <summary>
    /// <c>appsettings.Development.json</c> is loaded only when <c>DOTNET_ENVIRONMENT</c> is
    /// Development, never by a deployed host, and it is where the dev-only signing key and the
    /// client secrets matching the realm import live on purpose.
    /// </summary>
    private static bool IsDevelopmentOverlay(string path) =>
        Path.GetFileName(path).Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<SettingsFile> TrackedSettingsFiles() =>
        Git("ls-files", "-z", "--", "api", "tools")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                           && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SettingsFile(path, Parse(Git("show", $":{path}"))))
            .ToList();

    private static JsonDocument Parse(string json) =>
        JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

    /// <summary>
    /// Runs git at the repository root. A failure throws rather than degrading to the working copy:
    /// a guard that silently fell back would still be green on the machine where it stopped reading
    /// what it claims to, which is the one outcome worse than being red.
    /// </summary>
    private static string Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start git; the tracked-secrets guard cannot run.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
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
                   "Could not find ExpertToJob.slnx above the test binary; the tracked-secrets guard cannot run.");
    }

    private sealed record Leaf(string KeyPath, string Value);

    private sealed record SettingsFile(string Path, JsonDocument Json);

    /// <summary>One documented exception: file, key path and the exact value excused.</summary>
    private sealed record AllowedValue(string File, string KeyPath, string Value);
}
